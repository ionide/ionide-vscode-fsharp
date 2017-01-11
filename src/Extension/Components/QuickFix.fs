namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open Ionide.VSCode.Helpers

open DTO
open Ionide.VSCode.Helpers

module QuickFix =

    let private mkCommand name (document : TextDocument) (range : vscode.Range) (title : string) (suggestion : string) =
        let cmd = createEmpty<Command>
        cmd.title <- title
        cmd.command <- name
        cmd.arguments <- Some ([| document |> unbox; range |> unbox; suggestion |> unbox; |] |> ResizeArray)
        cmd

    let private mkQuickFix =
        mkCommand "fsharp.quickFix"

    let private mkRenameFix =
        mkCommand "fsharp.renameFix"

    let ifDiagnostic selector (f : Diagnostic -> Command[])  (diagnostics : Diagnostic seq) =
        let diagnostic = diagnostics |> Seq.tryFind (fun d -> d.message.Contains selector)
        match diagnostic with
        | None -> [||]
        | Some d -> f d


    let private getSuggestions doc (diagnostics : Diagnostic seq) =
        diagnostics
        |> ifDiagnostic "Maybe you want one of the following:" (fun d ->
            d.message.Split('\n').[2..]
            |> Array.map (fun suggestion ->
                let s = suggestion.Trim()
                let tiltle = sprintf "Replace with %s" s
                mkQuickFix doc d.range tiltle s
        ))

    let private getNewKeywordSuggestions (doc : TextDocument) (diagnostics : Diagnostic seq) =
        diagnostics
        |> ifDiagnostic "It is recommended that objects supporting the IDisposable interface are created using the syntax" (fun d ->
            let s = "new " + doc.getText(d.range)
            [| mkQuickFix doc d.range "Add new" s |] )

    let private fixUnused (doc : TextDocument) (diagnostics : Diagnostic seq) =
        diagnostics
        |> ifDiagnostic "is unused" (fun d ->
            let s = "_"
            let s2 =  "_" + doc.getText(d.range)
            [| mkQuickFix doc d.range "Replace with _" s
               mkQuickFix doc d.range "Prefix with _" s2 |] )


    let upercaseDU (doc : TextDocument) (diagnostics : Diagnostic seq) =
        diagnostics
        |> ifDiagnostic "Discriminated union cases and exception labels must be uppercase identifiers" (fun d ->
            let s = doc.getText(d.range).ToCharArray()
            let c = s.[0]?toUpperCase() |> unbox<char>

            let chars = [| yield c; yield! s.[1..]  |]
            let s = String(chars)

            [| mkRenameFix doc d.range (sprintf "Replace with %s" s) s |] )


    let private createProvider () =
        { new CodeActionProvider
          with
            member this.provideCodeActions(doc, range, context, ct) =
                let diagnostics = context.diagnostics
                [|
                    getSuggestions
                    getNewKeywordSuggestions
                    fixUnused
                    upercaseDU
                |] |> Array.collect (fun f -> f doc diagnostics) |> ResizeArray |> Case1
            }

    let applyQuickFix(doc : TextDocument, range : vscode.Range, suggestion : string) =
        let edit = WorkspaceEdit()
        let uri = Uri.file doc.fileName
        edit.replace(uri, range, suggestion)
        workspace.applyEdit edit

    let applyRenameFix(doc : TextDocument, range : vscode.Range, suggestion : string) =
        commands.executeCommand("vscode.executeDocumentRenameProvider", Uri.file doc.fileName, range.start, suggestion)
        |> Promise.bind (workspace.applyEdit)


    let activate selector (disposables: Disposable[]) =
        languages.registerCodeActionsProvider (selector, createProvider()) |> ignore
        commands.registerCommand("fsharp.quickFix",Func<obj,obj,obj,obj>(fun a b c -> applyQuickFix(a |> unbox, b |> unbox, c |> unbox) |> unbox )) |> ignore
        commands.registerCommand("fsharp.renameFix",Func<obj,obj,obj,obj>(fun a b c -> applyRenameFix(a |> unbox, b |> unbox, c |> unbox) |> unbox )) |> ignore

        ()



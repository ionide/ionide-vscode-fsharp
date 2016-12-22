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

    let private getSuggestions doc (diagnostics : Diagnostic seq) =
        let diagnostic = diagnostics |> Seq.tryFind (fun d -> d.message.Contains "Maybe you want one of the following:")
        match diagnostic with
        | None -> [||]
        | Some d ->
            d.message.Split('\n').[2..]
            |> Array.map (fun suggestion ->
                let s = suggestion.Trim()
                let cmd = createEmpty<Command>
                cmd.title <- sprintf "Replace with %s" s
                cmd.command <- "fsharp.quickFix"
                cmd.arguments <- Some ([| doc |> unbox; d.range |> unbox; s |> unbox; |] |> ResizeArray)
                cmd
            )
    let private getNewKeywordSuggestions (doc : TextDocument) (diagnostics : Diagnostic seq) =
        let diagnostic = diagnostics |> Seq.tryFind (fun d -> d.message.Contains "It is recommended that objects supporting the IDisposable interface are created using the syntax")
        match diagnostic with
        | None -> [||]
        | Some d ->
            let cmd = createEmpty<Command>
            let s = "new " + doc.getText(d.range)
            cmd.title <- sprintf "Add new"
            cmd.command <- "fsharp.quickFix"
            cmd.arguments <- Some ([| doc |> unbox; d.range |> unbox; s |> unbox; |] |> ResizeArray)
            [| cmd |]


    let private createProvider () =

        { new CodeActionProvider
          with
            member this.provideCodeActions(doc, range, context, ct) =
                let diagnostics = context.diagnostics
                seq {
                    yield! getSuggestions doc diagnostics
                    yield! getNewKeywordSuggestions doc diagnostics
                } |> ResizeArray |> Case1
            }

    let applyQuickFix(doc : TextDocument, range : vscode.Range, suggestion : string) =
        let edit = WorkspaceEdit()
        let uri = Uri.file doc.fileName
        edit.replace(uri, range, suggestion)
        workspace.applyEdit edit


    let activate selector (disposables: Disposable[]) =
        languages.registerCodeActionsProvider (selector, createProvider()) |> ignore
        commands.registerCommand("fsharp.quickFix",Func<obj,obj,obj,obj>(fun a b c -> applyQuickFix(a |> unbox, b |> unbox, c |> unbox) |> unbox )) |> ignore


        ()


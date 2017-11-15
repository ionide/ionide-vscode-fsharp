namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers

module UnusedDeclarations =
    let mutable private currentDiagnostic = languages.createDiagnosticCollection ()
    let refresh = EventEmitter<string>()
    let private isAnalyzerEnabled () = "FSharp.unusedDeclarationsAnalyzer" |> Configuration.get true

    let private diagnosticFromRange file (warning : UnusedDeclaration) =
        let range = CodeRange.fromDTO warning.Range
        let loc = Location (Uri.file file, range |> U2.Case1)
        let d = Diagnostic(range, "This value is unused", DiagnosticSeverity.Information)
        let code = if warning.IsThisMember then 1. else 0.
        d.code <- U2.Case2 code
        d, file

    let private mapResult file (ev : UnusedDeclarationsResult) =
        if isNotNull ev then
            ev.Data.Declarations
            |> Seq.map (diagnosticFromRange file)
            |> ResizeArray
        else
            ResizeArray ()

    let private analyzeDocument path =
        if isAnalyzerEnabled () then
            LanguageService.unusedDeclarations path
            |> Promise.onSuccess (fun (ev : UnusedDeclarationsResult) ->
                if isNotNull ev then
                    (Uri.file path, mapResult path ev |> Seq.map fst |> ResizeArray) |> currentDiagnostic.set)
            |> ignore

    let private handler filename =
        analyzeDocument filename

    let private createProvider () =

        { new CodeActionProvider
          with
            member __.provideCodeActions(doc, range, context, _) =
                let diagnostics = context.diagnostics
                let diagnostic = diagnostics |> Seq.tryFind (fun d -> d.message.Contains "This value is unused")
                let res =
                    match diagnostic with
                    | None -> [||]
                    | Some d ->
                        let txt = doc.getText range
                        let cmd = createEmpty<Command>
                        let replacer = if d.code = unbox 1. then "__" else "_"
                        cmd.title <- sprintf "Replace with %s" replacer
                        cmd.command <- "fsharp.unusedDeclarationsFix"
                        cmd.arguments <- Some ([| doc |> unbox; d.range |> unbox; replacer |> unbox |] |> ResizeArray)
                        let cmd2 = createEmpty<Command>
                        cmd2.title <- sprintf "Prefix with _"
                        cmd2.command <- "fsharp.unusedDeclarationsFix"
                        cmd2.arguments <- Some ([| doc |> unbox; d.range |> unbox; ("_" + txt)  |> unbox |] |> ResizeArray)


                        [|cmd; cmd2 |]
                res |> ResizeArray |> U2.Case1
            }

    let private applyQuickFix(doc : TextDocument, range : vscode.Range, suggestion : string) =
        let edit = WorkspaceEdit()
        let uri = Uri.file doc.fileName
        edit.replace(uri, range, suggestion)
        workspace.applyEdit edit
        |> Promise.onSuccess (fun _ -> analyzeDocument doc.fileName)

    let activate selector (context: ExtensionContext) =
        refresh.event $ (handler,(), context.subscriptions) |> ignore
        if JS.isDefined window.activeTextEditor then
            match window.activeTextEditor.document with
            | Document.FSharp -> refresh.fire window.activeTextEditor.document.fileName
            | _ -> ()

        languages.registerCodeActionsProvider (selector, createProvider()) |> context.subscriptions.Add
        commands.registerCommand("fsharp.unusedDeclarationsFix",Func<obj,obj,obj,obj>(fun a b c -> applyQuickFix(a |> unbox, b |> unbox, c |> unbox) |> unbox )) |> context.subscriptions.Add
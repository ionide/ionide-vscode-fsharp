namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers

module UnusedOpens =
    let mutable private currentDiagnostic = languages.createDiagnosticCollection ()
    let refresh = EventEmitter<string>()

    let private isAnalyzerEnabled () = "FSharp.unusedOpensAnalyzer" |> Configuration.get true

    let private diagnosticFromRange file (warning : Range) =
        let range = CodeRange.fromDTO warning
        let loc = Location (Uri.file file, range |> U2.Case1)
        Diagnostic(range, "Unused open statement", DiagnosticSeverity.Information), file

    let private mapResult file (ev : UnusedOpensResult) =
        if isNotNull ev then
            ev.Data.Declarations
            |> Seq.map (diagnosticFromRange file)
            |> ResizeArray
        else
            ResizeArray ()

    let private analyzeDocument path =
        if isAnalyzerEnabled () then
            LanguageService.lint path
            |> Promise.onSuccess (fun (ev : UnusedOpensResult) ->
                if isNotNull ev then
                    (Uri.file path, mapResult path ev |> Seq.map fst |> ResizeArray) |> currentDiagnostic.set)
            |> ignore

    let private handler filename =
        analyzeDocument filename

    let private createProvider () =

        { new CodeActionProvider
          with
            member this.provideCodeActions(doc, range, context, ct) =
                let diagnostics = context.diagnostics
                let diagnostic = diagnostics |> Seq.tryFind (fun d -> d.message.Contains "Unused open statement")
                let res =
                    match diagnostic with
                    | None -> [||]
                    | Some d ->
                        let cmd = createEmpty<Command>
                        cmd.title <- "Remove unused open"
                        cmd.command <- "fsharp.unusedOpenFix"
                        cmd.arguments <- Some ([| doc |> unbox; d.range |> unbox;  |] |> ResizeArray)
                        [|cmd |]
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
        commands.registerCommand("fsharp.unusedOpenFix",Func<obj,obj,obj,obj>(fun a b c -> applyQuickFix(a |> unbox, b |> unbox, c |> unbox) |> unbox )) |> context.subscriptions.Add
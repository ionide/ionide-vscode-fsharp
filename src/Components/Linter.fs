namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers

module Linter =
    let mutable private currentDiagnostic = languages.createDiagnosticCollection ()
    let private fixes = ResizeArray<Fix>()
    let refresh = EventEmitter<string>()

    let private isLinterEnabled () = "FSharp.linter" |> Configuration.get true

    let private diagnosticFromLintWarning file (warning : Lint) =
        let range = CodeRange.fromDTO warning.Range
        let loc = Location (Uri.file file, range |> U2.Case1)
        Diagnostic(range, "Lint: " + warning.Info, DiagnosticSeverity.Information), file

    let private mapResult file (ev : LintResult) =
        if isNotNull ev then
            ev.Data
            |> Seq.map (diagnosticFromLintWarning file)
            |> ResizeArray
        else
            ResizeArray ()

    let private lintDocument path =
        fixes.Clear()
        if isLinterEnabled () then
            LanguageService.lint path
            |> Promise.onSuccess (fun (ev : LintResult) ->
                if isNotNull ev then
                    ev.Data |> Array.where (fun a -> isNotNull a.Fix) |> Array.map (fun a ->a.Fix) |> fixes.AddRange
                    (Uri.file path, mapResult path ev |> Seq.map fst |> ResizeArray) |> currentDiagnostic.set)
            |> ignore

    let private handler filename =
        lintDocument filename

    let private createProvider () =

        { new CodeActionProvider
          with
            member this.provideCodeActions(doc, range, context, ct) =
                let diagnostics = context.diagnostics
                let diagnostic = diagnostics |> Seq.tryFind (fun d -> d.message.Contains "Lint:")
                let res =
                    match diagnostic with
                    | None -> [||]
                    | Some d ->
                        fixes
                        |> Seq.where (fun f ->  (unbox f.FromRange.StartColumn ) = d.range.start.character + 1. &&
                                                (unbox f.FromRange.EndColumn ) = d.range.``end`` .character + 1. &&
                                                (unbox f.FromRange.StartLine ) = d.range.start.line + 1. &&
                                                (unbox f.FromRange.EndLine ) = d.range.``end`` .line + 1.)
                        |> Seq.map (fun suggestion ->
                            let cmd = createEmpty<Command>
                            cmd.title <- sprintf "Replace with %s" suggestion.ToText
                            cmd.command <- "fsharp.lintFix"
                            cmd.arguments <- Some ([| doc |> unbox; d.range |> unbox; suggestion.ToText |> unbox; |] |> ResizeArray)
                            cmd)
                        |> Seq.toArray
                res |> ResizeArray |> U2.Case1
            }

    let private applyQuickFix(doc : TextDocument, range : vscode.Range, suggestion : string) =
        let edit = WorkspaceEdit()
        let uri = Uri.file doc.fileName
        edit.replace(uri, range, suggestion)
        workspace.applyEdit edit
        |> Promise.onSuccess (fun _ -> lintDocument doc.fileName)

    let activate selector (context: ExtensionContext) =
        refresh.event $ (handler,(), context.subscriptions) |> ignore
        if JS.isDefined window.activeTextEditor then
            match window.activeTextEditor.document with
            | Document.FSharp -> refresh.fire window.activeTextEditor.document.fileName
            | _ -> ()

        languages.registerCodeActionsProvider (selector, createProvider()) |> context.subscriptions.Add
        commands.registerCommand("fsharp.lintFix",Func<obj,obj,obj,obj>(fun a b c -> applyQuickFix(a |> unbox, b |> unbox, c |> unbox) |> unbox )) |> context.subscriptions.Add
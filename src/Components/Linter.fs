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

    let private isLinterEnabled () = "FSharp.linter" |> Configuration.get true

    let private diagnosticFromLintWarning file (warning : Lint) =
        let range = CodeRange.fromDTO warning.Range
        let loc = Location (Uri.file file, range |> Case1)
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
        LanguageService.lint path
        |> Promise.onSuccess (fun (ev : LintResult) ->
            if isNotNull ev then
                ev.Data |> Array.where (fun a -> isNotNull a.Fix) |> Array.map (fun a ->a.Fix) |> fixes.AddRange
                (Uri.file path, mapResult path ev |> Seq.map fst |> ResizeArray) |> currentDiagnostic.set)

    let mutable private timer = None

    let private handler (event : TextDocumentChangeEvent) =
        timer |> Option.iter(clearTimeout)
        match event.document with
        | Document.FSharp when  isLinterEnabled () ->
            timer <- Some (setTimeout((fun _ -> lintDocument event.document.fileName |> ignore), 1000.))
        | _ -> ()

    let private handlerOpen (event : TextEditor) =
        if JS.isDefined event then
            match event.document with
            | Document.FSharp when isLinterEnabled () ->
                lintDocument event.document.fileName |> ignore
            | _ -> ()

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
                res |> ResizeArray |> Case1
            }

    let private applyQuickFix(doc : TextDocument, range : vscode.Range, suggestion : string) =
        let edit = WorkspaceEdit()
        let uri = Uri.file doc.fileName
        edit.replace(uri, range, suggestion)
        workspace.applyEdit edit

    let activate selector (disposables: Disposable[]) =
        workspace.onDidChangeTextDocument $ (handler,(), disposables) |> ignore
        window.onDidChangeActiveTextEditor $ (handlerOpen, (), disposables) |> ignore
        window.visibleTextEditors |> Seq.iter (handlerOpen)

        languages.registerCodeActionsProvider (selector, createProvider()) |> ignore
        commands.registerCommand("fsharp.lintFix",Func<obj,obj,obj,obj>(fun a b c -> applyQuickFix(a |> unbox, b |> unbox, c |> unbox) |> unbox )) |> ignore

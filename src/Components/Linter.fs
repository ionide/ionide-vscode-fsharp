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
        LanguageService.lint path
        |> Promise.onSuccess (fun (ev : LintResult) ->  (Uri.file path, mapResult path ev |> Seq.map fst |> ResizeArray) |> currentDiagnostic.set)

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

    let activate (disposables: Disposable[]) =
        workspace.onDidChangeTextDocument $ (handler,(), disposables) |> ignore
        window.onDidChangeActiveTextEditor $ (handlerOpen, (), disposables) |> ignore
        window.visibleTextEditors |> Seq.iter (handlerOpen)

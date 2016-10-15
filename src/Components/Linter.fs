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

    [<Emit("setTimeout($0,$1)")>]
    let setTimeout(cb, delay) : obj = failwith "JS Only"

    [<Emit("clearTimeout($0)")>]
    let clearTimeout(timer) : unit = failwith "JS Only"

    let mutable private currentDiagnostic = languages.createDiagnosticCollection ()

    let private isLinterEnabled () = "FSharp.linter" |> Configuration.get true

    let private diagnosticFromLintWarning file (warning : Lint) =
        let range = CodeRange.fromDTO warning.Range
        let loc = Location (Uri.file file, range |> Case1)
        Diagnostic(range, "Lint: " + warning.Info, DiagnosticSeverity.Information), file

    let private mapResult file (ev : LintResult) =
        let res =
            if unbox ev <> null then
                ev.Data
                |> Seq.map (diagnosticFromLintWarning file)
                |> ResizeArray
            else
                ResizeArray ()
        Browser.console.log res
        res

    let private parse path =
        LanguageService.lint path
        |> Promise.onSuccess (fun (ev : LintResult) ->  (Uri.file path, mapResult path ev |> Seq.map fst |> ResizeArray) |> currentDiagnostic.set)

    let mutable private timer = None

    let private handler (event : TextDocumentChangeEvent) =
        timer |> Option.iter(clearTimeout)
        match event.document with
        | Document.FSharp when  isLinterEnabled () ->
            timer <- Some (setTimeout((fun _ -> parse event.document.fileName |> ignore), 500.))
        | _ -> ()

    let private handlerOpen (event : TextEditor) =
        match event.document with
        | Document.FSharp when  isLinterEnabled () && JS.isDefined event ->
            parse event.document.fileName |> ignore
        | _ -> ()

    let activate (disposables: Disposable[]) =
        workspace.onDidChangeTextDocument $ (handler,(), disposables) |> ignore
        window.onDidChangeActiveTextEditor $ (handlerOpen, (), disposables) |> ignore
        window.visibleTextEditors |> Seq.iter (handlerOpen)

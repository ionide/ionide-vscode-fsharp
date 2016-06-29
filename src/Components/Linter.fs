namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers


module Linter =

    let mutable private currentDiagnostic = languages.createDiagnosticCollection ()

    let private parse path text =

        let mapResult (ev : ParseResult) =
            ev.Data
            |> Seq.distinctBy (fun d -> d.Severity, d.StartLine, d.StartColumn)
            |> Seq.choose (fun d ->
                try
                    let range = Range(float d.StartLine - 1., float d.StartColumn - 1., float d.EndLine - 1., float d.EndColumn - 1.)
                    let loc = Location (Uri.file d.FileName, range |> Case1)
                    let severity = if d.Severity = "Error" then 0 else 1
                    Diagnostic(range, d.Message, unbox severity) |> Some
                with
                | _ -> None )
            |> ResizeArray

        LanguageService.parse path text
        |> Promise.success (fun (ev : ParseResult) ->  (Uri.file path, mapResult ev) |> currentDiagnostic.set  )


    let parseFile (file : TextDocument) =
        if file.languageId = "fsharp" then
            let path = file.fileName
            let prom = Project.find path
            match prom with
            | Some p -> p
                        |> LanguageService.project
                        |> Promise.bind (fun _ -> parse path (file.getText ()))
            | None -> parse path (file.getText ())
        else
            Promise.lift (null |> unbox)


    let mutable private timer = None : float option

    let private handler (event : TextDocumentChangeEvent) =
        timer |> Option.iter(Browser.window.clearTimeout)
        timer <- Some (Browser.window.setTimeout((fun _ ->
            if event.document.languageId = "fsharp" then
                parse (event.document.fileName) (event.document.getText ()) |> ignore), 500.) )


    let private handlerOpen (event : TextEditor) =
        if JS.isDefined event then
            parseFile event.document
        else
            Promise.lift ()

    let activate (disposables: Disposable[]) =
        workspace.onDidChangeTextDocument $ (handler,(), disposables) |> ignore

        window.onDidChangeActiveTextEditor $ (handlerOpen, (), disposables) |> ignore

        match window.visibleTextEditors |> Seq.toList with
        | [] -> Promise.lift (null |> unbox)
        | [x] -> parseFile x.document
        | x::tail ->
            tail
            |> List.fold (fun acc e -> acc |> Promise.bind(fun _ -> parseFile e.document ) )
               (parseFile x.document )
        |> ignore

namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers


module Errors =
    let mutable private currentDiagnostic = languages.createDiagnosticCollection ()

    let private mapResult (ev : ParseResult) =
        ev.Data
        |> Seq.distinctBy (fun error -> error.Severity, error.StartLine, error.StartColumn)
        |> Seq.choose (fun error ->
            try
                let range = CodeRange.fromError error
                let loc = Location (Uri.file error.FileName, range |> Case1)
                let severity = if error.Severity = "Error" then 0 else 1
                (Diagnostic(range, error.Message, unbox severity), error.FileName) |> Some
            with
            | _ -> None )
        |> ResizeArray

    let private parse path text version =
        LanguageService.parse path text version
        |> Promise.map (fun (ev : ParseResult) ->  (Uri.file path, mapResult ev |> Seq.map fst |> ResizeArray) |> currentDiagnostic.set  )


    let parseFile (file : TextDocument) =
        match file with
        | Document.FSharp ->
            let path = file.fileName
            let prom = Project.find path
            match prom with
            | Some p -> p
                        |> Project.load
                        |> Promise.bind (fun _ -> parse path (file.getText ()) file.version)
            | None -> parse path (file.getText ()) file.version
        | _ -> Promise.lift (null |> unbox)

    let parseProject () =
        promise {
            let! res = LanguageService.parseProject ()
            res
            |> mapResult
            |> fun n ->
                currentDiagnostic.clear ()
                n
            |> Seq.groupBy(fun (x,p) -> p)
            |> Seq.iter (fun (path, ev) ->  (Uri.file path, ev |> Seq.map fst |> ResizeArray) |> currentDiagnostic.set)

        }

    let mutable private timer = None

    let private handler (event : TextDocumentChangeEvent) =
        timer |> Option.iter(clearTimeout)
        timer <- Some (setTimeout((fun _ ->
            match event.document with
            | Document.FSharp ->  parse (event.document.fileName) (event.document.getText ()) event.document.version
            | _ -> promise { () } ), 500.))



    let private handlerOpen (event : TextEditor) =
        if JS.isDefined event then
            parseFile event.document
        else
            Promise.lift ()

    let activate (disposables: Disposable[]) =
        workspace.onDidChangeTextDocument $ (handler,(), disposables) |> ignore
        workspace.onDidSaveTextDocument $ (parseProject, (), disposables) |> ignore
        window.onDidChangeActiveTextEditor $ (handlerOpen, (), disposables) |> ignore


        match window.visibleTextEditors |> Seq.toList with
        | [] -> Promise.lift (null |> unbox)
        | [x] -> parseFile x.document
        | x::tail ->
            tail
            |> List.fold (fun acc e -> acc |> Promise.bind(fun _ -> parseFile e.document ) )
               (parseFile x.document )
        |> Promise.bind (parseProject)

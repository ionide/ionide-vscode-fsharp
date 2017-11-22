namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers
open System.Text.RegularExpressions

module Errors =
    let private logger = ConsoleAndOutputChannelLogger(Some "Errors", Level.DEBUG, None, Some Level.DEBUG)

    let mutable private currentDiagnostic = languages.createDiagnosticCollection ()

    let private mapResult (ev : ParseResult) =
        let errors =
            ev.Data.Errors
            |> Seq.distinctBy (fun error -> error.Severity, error.StartLine, error.StartColumn)
            |> Seq.choose (fun error ->
                try
                    if error.FileName |> String.startWith "\\" then None else
                    let range = CodeRange.fromError error
                    let loc = Location (Uri.file error.FileName, range |> U2.Case1)
                    let severity = if error.Severity = "Error" then 0 else 1
                    (Diagnostic(range, error.Message, unbox severity), error.FileName) |> Some
                with
                | _ -> None )
            |> ResizeArray
        ev.Data.File, errors

    type DocumentParsedEvent = {
        fileName: string
        text: string
        version: float
        /// BEWARE: Live object, might have changed since the parsing
        document: TextDocument
        result: ParseResult
    }

    let private onDocumentParsedEmitter = EventEmitter<DocumentParsedEvent>()
    let onDocumentParsed = onDocumentParsedEmitter.event;

    let private parse (document : TextDocument) =
        let fileName = document.fileName
        let text = document.getText()
        let version = document.version
        LanguageService.parse document.fileName (document.getText()) document.version
        |> Promise.map (fun (result : ParseResult) ->
            if isNotNull result then
                onDocumentParsedEmitter.fire { fileName = fileName; text = text; version = version; document = document; result = result }
                CodeLens.refresh.fire (unbox version)
                Linter.refresh.fire fileName
                UnusedOpens.refresh.fire fileName
                UnusedDeclarations.refresh.fire fileName
                SimplifyName.refresh.fire fileName
                CodeOutline.refresh.fire (undefined)
                (Uri.file fileName, (mapResult result |> snd |> Seq.map fst |> ResizeArray)) |> currentDiagnostic.set
                Some fileName
            else
                None)

    let private parseFile (file : TextDocument) =
        match file with
        | Document.FSharp ->
            let path = file.fileName
            match Project.find path with
            | Choice1Of3 _ -> parse file
            | Choice2Of3 () -> Promise.lift None
            | Choice3Of3 (Some p) ->
                p
                |> Project.load
                |> Promise.bind (fun _ -> parse file)
            | Choice3Of3 None -> parse file
        | _ -> Promise.lift None

    let mutable private timer = None

    let timeout (lines : float) =
        if lines < 200. then 10.
        elif lines < 1000. then 100.
        elif lines < 2000. then 200.
        else 500.

    let private handler (event : TextDocumentChangeEvent) =
        let t = timeout event.document.lineCount
        timer |> Option.iter(clearTimeout)
        timer <- Some (setTimeout (fun _ ->

            match event.document with
            | Document.FSharp ->
                parse event.document
                |> ignore
            | _ -> () ) t)

    let private handlerOpen (event : TextEditor) =
        if JS.isDefined event then
            parseFile event.document
            |> Promise.bind (fun n ->
                match n with
                | Some _fileName -> LanguageService.projectsInBackground event.document.fileName
                | None -> Promise.lift ()
            )
        else
            Promise.lift ()

    let private handlerSave (event : TextDocument) =
        if JS.isDefined event then
            parseFile event
            |> Promise.bind (fun n ->
                match n with
                | Some _fileName -> LanguageService.projectsInBackground event.fileName
                | None -> Promise.lift ()
            )
        else
            Promise.lift ()


    let private handleNotification res =
        printfn "NOTIFY: %A" res
        let (file, errors) = mapResult res
        let notActive =
            match unbox window.activeTextEditor with
            | None -> true
            | Some (e: TextEditor) -> e.document.fileName <> file
        if notActive then
            currentDiagnostic.set(Uri.file file, errors |> Seq.map fst |> ResizeArray)

    let parseVisibleTextEditors () =
        match window.visibleTextEditors |> Seq.toList with
        | [] -> Promise.lift (null |> unbox)
        | [x] -> parseFile x.document
        | x::tail ->
            tail |> List.fold (fun acc e -> acc |> Promise.bind(fun _ -> parseFile e.document )) (parseFile x.document )


    let activate (context: ExtensionContext) =
        workspace.onDidChangeTextDocument $ (handler,(), context.subscriptions) |> ignore
        window.onDidChangeActiveTextEditor $ (handlerOpen, (), context.subscriptions) |> ignore
        workspace.onDidSaveTextDocument $ (handlerSave, (), context.subscriptions) |> ignore
        LanguageService.registerNotify handleNotification
        Promise.lift parseVisibleTextEditors




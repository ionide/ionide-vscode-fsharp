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

    let private logger = ConsoleAndOutputChannelLogger(Some "Errors", Level.DEBUG, None, Some Level.DEBUG)

    let mutable customDelay = -1.

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

    type DocumentParsedEvent =
        { fileName : string
          text : string
          version : float
          /// BEWARE: Live object, might have changed since the parsing
          document : TextDocument
          result : ParseResult }

    let private onDocumentParsedEmitter = EventEmitter<DocumentParsedEvent>()
    let onDocumentParsed = onDocumentParsedEmitter.event;

    let private parse (document : TextDocument) =
        let fileName = document.fileName
        let text = document.getText()
        let version = document.version
        let uri = document.uri
        LanguageService.parse document.fileName (document.getText()) document.version
        |> Promise.map (fun (result : ParseResult) ->
            if isNotNull result then
                onDocumentParsedEmitter.fire { fileName = fileName; text = text; version = version; document = document; result = result }
                CodeLens.refresh.fire (unbox version)
                Linter.refresh.fire fileName
                UnusedOpens.refresh.fire fileName
                UnusedDeclarations.refresh.fire fileName
                SimplifyName.refresh.fire fileName
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
                |> Project.load false
                |> Promise.bind (fun _ -> parse file)
            | Choice3Of3 None -> parse file
        | _ -> Promise.lift None

    let mutable private timer = None

    let timeout (lines : float) =
        if customDelay = -1. then
            if lines < 200. then 50.
            elif lines < 1000. then 200.
            elif lines < 2000. then 400.
            else 800.
        else
            customDelay

    let private handler (event : TextDocumentChangeEvent) =
        let t = timeout event.document.lineCount
        timer |> Option.iter(clearTimeout)
        timer <- Some (setTimeout (fun _ ->

            match event.document with
            | Document.FSharp ->
                parse event.document
                |> ignore
            | _ -> () ) t)

    let private handlerOpen allowBackgroundParsing (event : TextEditor) =
        if JS.isDefined event then
            match event.document with
            | Document.FSharp ->
                promise {
                    let fileInProj =
                        if path.extname event.document.fileName = ".fsx" then
                            true
                        else
                            Project.getLoaded ()
                            |> List.exists (fun n ->
                                n.Files |> List.exists (fun p -> path.normalize p = path.normalize event.document.fileName ))

                    let notifyFsNotInProject = "FSharp.notifyFsNotInFsproj" |> Configuration.get true
                    let! _ =
                        if (not fileInProj) && notifyFsNotInProject then
                            promise {
                                let! res = window.showWarningMessage(sprintf "File %s can't be found in any parsed project. Usually .fs files should be included in .fsproj file" (path.basename event.document.fileName), "Disable notification", "OK"  )
                                let! _ =
                                    if res = "Disable notification" then
                                        Configuration.set "FSharp.notifyFsNotInFsproj" false
                                    else
                                        Promise.empty
                                return ()
                            }
                        else
                            Promise.empty

                    let! parseResult = parseFile event.document

                    if allowBackgroundParsing then
                        match parseResult with
                        | Some _fileName ->
                            return! LanguageService.projectsInBackground event.document.fileName
                        | None ->
                            return ()
                }
            | _ -> Promise.empty
        else
            Promise.empty

    let private handlerSave allowBackgroundParsing (event : TextDocument) =
        if JS.isDefined event then
            promise {
                let! parseResult = parseFile event

                if allowBackgroundParsing then
                    match parseResult with
                    | Some _fileName ->
                        return! LanguageService.projectsInBackground event.fileName
                    | None ->
                        return ()
            }
        else
            Promise.lift ()

    let private handleNotification res =
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

    let parseVisibleFileInProject (proj : Project) =
        let vs =
            window.visibleTextEditors
            |> Seq.where (fun te -> proj.Files |> List.contains te.document.fileName)
            |> Seq.toList
        setTimeout (fun _ ->
            match vs with
            | [] -> ()
            | [x] -> parse x.document |> ignore
            | x::tail ->
                tail |> List.fold (fun acc e -> acc |> Promise.bind(fun _ -> parse e.document )) (parse x.document ) |> ignore)  250.

    let private deleteWatcher = workspace.createFileSystemWatcher("**/*.{fs,fsx}", true, true, false)


    let activate (context : ExtensionContext) =
        let d = ("FSharp.customTypecheckingDelay" |> Configuration.get -1.)
        customDelay <- d
        let allowBackgroundParsing = not ("FSharp.minimizeBackgroundParsing" |> Configuration.get false)
        Project.projectLoaded.event $ (parseVisibleFileInProject, (), context.subscriptions) |> ignore


        deleteWatcher.onDidDelete $ (fun (uri : Uri) ->
            currentDiagnostic.delete uri
            Linter.deleteDiagnostic uri
            UnusedOpens.deleteDiagnostic uri
            UnusedDeclarations.deleteDiagnostic uri
            SimplifyName.deleteDiagnostic uri
            Analyzers.deleteDiagnostic uri
        ) |> ignore

        workspace.onDidChangeTextDocument $ (handler, (), context.subscriptions) |> ignore
        window.onDidChangeActiveTextEditor $ (handlerOpen allowBackgroundParsing, (), context.subscriptions) |> ignore
        workspace.onDidSaveTextDocument $ (handlerSave allowBackgroundParsing, (), context.subscriptions) |> ignore
        LanguageService.registerNotify handleNotification
        Promise.lift parseVisibleTextEditors

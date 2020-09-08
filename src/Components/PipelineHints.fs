module Ionide.VSCode.FSharp.PipelineHints

open System
open System.Collections.Generic
open Fable.Core
open Fable.Import.vscode
open global.Node
open Fable.Core.JsInterop
open DTO

type Number = float

let private logger = ConsoleAndOutputChannelLogger(Some "PipelineHints", Level.DEBUG, None, Some Level.DEBUG)

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module PipelineHintsConfig =

    type PipelineHintsConfig =
        { enabled : bool
          prefix : string }

    let defaultConfig =
        { enabled = true
          prefix = " //  " }

    let getConfig () =
        let cfg = workspace.getConfiguration()

        { enabled = cfg.get("FSharp.pipelineHints.enabled", defaultConfig.enabled)
          prefix = cfg.get("FSharp.pipelineHints.prefix", defaultConfig.prefix) }


module Documents =

    type Cached =
        { /// vscode document version that was parsed
          version : Number
          /// Decorations
          decorations : ResizeArray<DecorationOptions>
          /// Text editors where the decorations are shown
          textEditors : ResizeArray<TextEditor> }

    type DocumentInfo =
        { /// Full path of the document
          fileName : string
          /// Current decoration cache
          cache : Cached option }

    type Documents = Dictionary<string, DocumentInfo>

    let inline create () = Documents()

    let inline tryGet fileName (documents : Documents) = documents.TryGet fileName

    let inline getOrAdd fileName (documents : Documents) =
        match tryGet fileName documents with
        | Some x -> x
        | None ->
            let value = { fileName = fileName; cache = None }
            documents.Add(fileName, value)
            value

    let inline set fileName value (documents : Documents) = documents.[fileName] <- value

    let update info (decorations : ResizeArray<DecorationOptions>) version (documents : Documents) =
        let updated =
            { info with
                cache =
                    Some {
                        version = version
                        decorations = decorations
                        textEditors = ResizeArray() } }
        documents |> set info.fileName updated
        updated

    let inline tryGetCached fileName (documents : Documents) =
        documents
        |> tryGet fileName
        |> Option.bind(fun info -> info.cache |> Option.map(fun c -> info, c))

    let inline tryGetCachedAtVersion fileName version (documents : Documents) =
        documents
        |> tryGet fileName
        |> Option.bind(fun info ->
            match info.cache with
            | Some cache when cache.version = version -> Some (info, cache)
            | _ -> None
            )

let mutable private config = PipelineHintsConfig.defaultConfig

module PipelineHintsDecorations =

    let create range text =
        // What we add after the range
        let attachment = createEmpty<ThemableDecorationAttachmentRenderOptions>
        attachment.color <- Some (U2.Case2 (ThemeColor "fsharp.pipelineHints"))
        attachment.contentText <- Some text

        // Theme for the range
        let renderOptions = createEmpty<DecorationInstanceRenderOptions>
        renderOptions.after <- Some attachment

        let decoration = createEmpty<DecorationOptions>
        decoration.range <- range
        decoration.renderOptions <- Some renderOptions
        decoration

    let decorationType =
        let opt = createEmpty<DecorationRenderOptions>
        opt.isWholeLine <- Some true
        opt

type State =
    { documents : Documents.Documents
      decorationType : TextEditorDecorationType
      disposables : ResizeArray<Disposable> }

module DecorationUpdate =

    let interestingSymbolPositions (doc : TextDocument) (lines : PieplineHint[]) : (CodeRange.CodeRange * string []) []  =
        lines
        |> Array.map (fun n ->
            let textLine = doc.lineAt (float n.Line)
            textLine.range, n.Types
        )

    let private getSignature (range : CodeRange.CodeRange, tts: string []) =
        let tt = tts.[0]
        let id = tt.IndexOf("is")
        let res = tt.Substring(id + 3)
        range, "  " + res



    let private declarationsResultToSignatures (doc : TextDocument) (declarationsResult: DTO.PipelineHintsResult) fileName =
        promise {
            let interesting =
                declarationsResult.Data
                |> interestingSymbolPositions doc
            let signatures =
                interesting
                |> Array.map (getSignature)
            return signatures
        }

    /// Update the decorations stored for the document.
    /// * If the info is already in cache, return that
    /// * If it change during the process nothing is done and it return None, if a real change is done it return the new state
    let updateDecorationsForDocument (document : TextDocument) (version : float) state =
        promise {
            let fileName = document.fileName

            match state.documents |> Documents.tryGetCachedAtVersion fileName version with
            | Some (info, _) ->
                logger.Debug("Found existing decorations in cache for '%s' @%d", fileName, version)
                return Some info
            | None when document.version = version ->
                let! hintsResults = LanguageService.pipelineHints fileName
                if document.version = version && isNotNull hintsResults then

                    let! signatures = declarationsResultToSignatures document hintsResults fileName
                    let info = state.documents |> Documents.getOrAdd fileName
                    if document.version = version && info.cache.IsNone || info.cache.Value.version <> version then
                        let decorations = signatures |> Seq.map (fun (r, s) -> PipelineHintsDecorations.create r (config.prefix + s)) |> ResizeArray

                        logger.Debug("New decorations generated for '%s' @%d", fileName, version)
                        return Some (state.documents |> Documents.update info decorations version)
                    else
                        return None
                else
                    return None
            | _ ->
                return None
        }

    /// Set the decorations for the editor, filtering lines where the user recently typed
    let setDecorationsForEditor (textEditor : TextEditor) (info : Documents.DocumentInfo) state =
        match info.cache with
        | Some cache when not (cache.textEditors.Contains(textEditor))->
            cache.textEditors.Add(textEditor)
            logger.Debug("Setting decorations for '%s' @%d", info.fileName, cache.version)
            textEditor.setDecorations(state.decorationType, U2.Case2(cache.decorations))
        | _ -> ()

    /// Set the decorations for the editor if we have them for the current version of the document
    let setDecorationsForEditorIfCurrentVersion (textEditor : TextEditor) state =
        let fileName = textEditor.document.fileName
        let version = textEditor.document.version

        match Documents.tryGetCachedAtVersion fileName version state.documents with
        | None -> () // An event will arrive later when we have generated decorations
        | Some (info, _) -> setDecorationsForEditor textEditor info state

    let documentClosed (fileName : string) state =
        // We can/must drop all caches as versions are unique only while a document is open.
        // If it's re-opened later versions will start at 1 again.
        state.documents.Remove(fileName) |> ignore

let inline private isFsharpFile (doc : TextDocument) =
    match doc with
    | Document.FSharp when doc.uri.scheme = "file" -> true
    | Document.FSharpScript when doc.uri.scheme = "file"  -> true
    | _ -> false

let mutable private state: State option = None

let private textEditorsChangedHandler (textEditors : ResizeArray<TextEditor>) =
    match state with
    | Some state ->
        for textEditor in textEditors do
            if isFsharpFile textEditor.document then
                DecorationUpdate.setDecorationsForEditorIfCurrentVersion textEditor state
    | None -> ()

let private documentParsedHandler (event : Notifications.DocumentParsedEvent) =
    match state with
    | None -> ()
    | Some state ->
        promise {
            let! updatedInfo = DecorationUpdate.updateDecorationsForDocument event.document event.version state
            match updatedInfo with
            | Some info ->
                // Update all text editors where this document is shown (potentially more than one)
                window.visibleTextEditors
                |> Seq.filter (fun editor -> editor.document = event.document)
                |> Seq.iter(fun editor -> DecorationUpdate.setDecorationsForEditor editor info state)
            | _ -> ()
        } |> logger.ErrorOnFailed "Updating after parse failed"

let private closedTextDocumentHandler (textDocument : TextDocument) =
    state |> Option.iter (DecorationUpdate.documentClosed textDocument.fileName)

let install () =
    logger.Debug "Installing"

    let decorationType = window.createTextEditorDecorationType(PipelineHintsDecorations.decorationType)
    let disposables = ResizeArray<Disposable>()

    disposables.Add(window.onDidChangeVisibleTextEditors.Invoke(unbox textEditorsChangedHandler))
    disposables.Add(Notifications.onDocumentParsed.Invoke(unbox documentParsedHandler))
    disposables.Add(workspace.onDidCloseTextDocument.Invoke(unbox closedTextDocumentHandler))

    let newState = { decorationType = decorationType; disposables = disposables; documents = Documents.create() }

    state <- Some newState

    logger.Debug "Installed"

let uninstall () =
    logger.Debug "Uninstalling"

    match state with
    | None -> ()
    | Some state ->
        for disposable in state.disposables do
            disposable.dispose() |> ignore

        state.decorationType.dispose()

    state <- None
    logger.Debug "Uninstalled"

let configChangedHandler () =
    logger.Debug("Config Changed event")

    let wasEnabled = (config.enabled) && state <> None
    config <- PipelineHintsConfig.getConfig ()
    let isEnabled = config.enabled

    if wasEnabled <> isEnabled then
        if isEnabled then
            install ()
        else
            uninstall ()


let activate (context : ExtensionContext) =
    logger.Info "Activating"

    workspace.onDidChangeConfiguration $ (configChangedHandler, (), context.subscriptions) |> ignore

    configChangedHandler ()
    ()


let t =
    [1.. 10]
    |> List.filter (fun n -> n % 2 = 0)
    |> List.map (Some)
    |> List.mapi (fun i n -> Option.isSome n)
    |> List.isEmpty
    |> fun n -> "asd"
    |> fun n -> DateTime.Now

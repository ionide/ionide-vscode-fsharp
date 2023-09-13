module Ionide.VSCode.FSharp.LineLensShared

open System.Collections.Generic
open Fable.Core
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open Fable.Core.JsInterop
open Fable.Core.JS
open Logging

type Number = float


module Documents =

    type Cached =
        {
            /// vscode document version that was parsed
            version: Number
            /// Decorations
            decorations: ResizeArray<DecorationOptions>
            /// Text editors where the decorations are shown
            textEditors: ResizeArray<TextEditor>
        }

    type DocumentInfo =
        {
            /// Full uri of the document
            uri: Uri
            /// Current decoration cache
            cache: Cached option
        }

    type Documents = Dictionary<Uri, DocumentInfo>

    let inline create () = Documents()

    let inline tryGet uri (documents: Documents) = documents.TryGet uri

    let inline getOrAdd uri (documents: Documents) =
        match tryGet uri documents with
        | Some x -> x
        | None ->
            let value = { uri = uri; cache = None }
            documents.Add(uri, value)
            value

    let inline set uri value (documents: Documents) = documents.[uri] <- value

    let update info (decorations: ResizeArray<DecorationOptions>) version (documents: Documents) =
        let updated =
            { info with
                cache =
                    Some
                        { version = version
                          decorations = decorations
                          textEditors = ResizeArray() } }

        documents |> set info.uri updated
        updated

    let inline tryGetCached uri (documents: Documents) =
        documents
        |> tryGet uri
        |> Option.bind (fun info -> info.cache |> Option.map (fun c -> info, c))

    let inline tryGetCachedAtVersion uri version (documents: Documents) =
        documents
        |> tryGet uri
        |> Option.bind (fun info ->
            match info.cache with
            | Some cache when cache.version = version -> Some(info, cache)
            | _ -> None)

module LineLensDecorations =

    let create theme range text =
        // What we add after the range
        let attachment = createEmpty<ThemableDecorationAttachmentRenderOptions>
        attachment.color <- Some(U2.Case2(vscode.ThemeColor.Create theme))
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

type LineLensConfig = { enabled: bool; prefix: string }

type LineLensState =
    { documents: Documents.Documents
      decorationType: TextEditorDecorationType
      disposables: ResizeArray<Disposable> }

type DecorationUpdate =
    ConsoleAndOutputChannelLogger
        -> LineLensConfig
        -> (TextDocument)
        -> (float)
        -> LineLensState
        -> Promise<option<Documents.DocumentInfo>>

module DecorationUpdate =
    /// Update the decorations stored for the document.
    /// * If the info is already in cache, return that
    /// * If it change during the process nothing is done and it return None, if a real change is done it return the new state
    let updateDecorationsForDocument<'a>
        (fetchHintsData: Uri -> Promise<option<'a>>)
        (hintsToSignature: TextDocument -> 'a -> Uri -> Promise<(Range * string) array>)
        (signatureToDecoration: LineLensConfig -> TextDocument -> (Range * string) -> DecorationOptions)
        (logger: ConsoleAndOutputChannelLogger)
        config
        (document: TextDocument)
        (version: float)
        state
        =
        promise {
            let uri = document.uri

            match state.documents |> Documents.tryGetCachedAtVersion uri version with
            | Some(info, _) ->
                logger.Debug("Found existing decorations in cache for '%s' @%d", uri, version)
                return Some info
            | None when document.version = version ->
                logger.Debug("getting new decorations for '%s' @%d", uri, version)
                let! hintsResults = fetchHintsData uri

                match hintsResults with
                | None -> return None
                | Some hintsResults ->
                    if document.version = version then

                        let! signatures = hintsToSignature document hintsResults uri
                        let info = state.documents |> Documents.getOrAdd uri

                        if
                            document.version = version && info.cache.IsNone
                            || info.cache.Value.version <> version
                        then
                            let decorations =
                                signatures |> Seq.map (signatureToDecoration config document) |> ResizeArray

                            logger.Debug("New decorations generated for '%s' @%d", uri, version)

                            return Some(state.documents |> Documents.update info decorations version)
                        else
                            return None
                    else
                        return None
            | _ -> return None
        }



let inline private isFsharpFile (doc: TextDocument) =
    match doc with
    | Document.FSharp when doc.uri.scheme = "file" -> true
    | Document.FSharpScript when doc.uri.scheme = "file" -> true
    | _ -> false

///A generic type for a Decoration that is displayed at the end of a line
/// This is used in LineLens and PipelineHints
/// The bulk of the logic is the decorationUpdate function you provide
/// Normally this should be constructed using the `DecorationUpdate.updateDecorationsForDocument` function
/// which provides caching and filtering of the decorations
type LineLens
    (
        name,
        decorationUpdate: DecorationUpdate,
        getConfig: unit -> LineLensConfig,
        ?decorationType: DecorationRenderOptions
    ) =

    let logger =
        ConsoleAndOutputChannelLogger(Some $"LineLensRenderer-{name}", Level.DEBUG, None, Some Level.DEBUG)

    let decorationType =
        decorationType |> Option.defaultValue LineLensDecorations.decorationType

    let mutable config = { enabled = true; prefix = " // " }
    let mutable state: LineLensState option = None

    /// Set the decorations for the editor, filtering lines where the user recently typed
    let setDecorationsForEditor (textEditor: TextEditor) (info: Documents.DocumentInfo) state =
        match info.cache with
        | Some cache when not (cache.textEditors.Contains(textEditor)) ->
            cache.textEditors.Add(textEditor)
            logger.Debug("Setting decorations for '%s' @%d", info.uri, cache.version)
            textEditor.setDecorations (state.decorationType, U2.Case2(cache.decorations))
        | _ -> ()

    /// Set the decorations for the editor if we have them for the current version of the document
    let setDecorationsForEditorIfCurrentVersion (textEditor: TextEditor) state =
        let uri = textEditor.document.uri
        let version = textEditor.document.version

        match Documents.tryGetCachedAtVersion uri version state.documents with
        | None -> () // An event will arrive later when we have generated decorations
        | Some(info, _) -> setDecorationsForEditor textEditor info state

    let documentClosed (uri: Uri) state =
        // We can/must drop all caches as versions are unique only while a document is open.
        // If it's re-opened later versions will start at 1 again.
        state.documents.Remove(uri) |> ignore


    let textEditorsChangedHandler (textEditors: ResizeArray<TextEditor>) =
        match state with
        | Some state ->
            for textEditor in textEditors do
                if isFsharpFile textEditor.document then
                    setDecorationsForEditorIfCurrentVersion textEditor state
        | None -> ()

    let documentParsedHandler (event: Notifications.DocumentParsedEvent) =
        match state with
        | None -> ()
        | Some state ->
            promise {
                let! updatedInfo = decorationUpdate logger config event.document event.version state

                match updatedInfo with
                | Some info ->
                    // Update all text editors where this document is shown (potentially more than one)
                    window.visibleTextEditors
                    |> Seq.filter (fun editor -> editor.document = event.document)
                    |> Seq.iter (fun editor -> setDecorationsForEditor editor info state)
                | _ -> ()
            }
            |> logger.ErrorOnFailed "Updating after parse failed"

    let closedTextDocumentHandler (textDocument: TextDocument) =
        state |> Option.iter (documentClosed textDocument.uri)

    let install decorationType =
        logger.Debug "Installing"

        let decorationType = window.createTextEditorDecorationType (decorationType)

        let disposables = ResizeArray<Disposable>()

        disposables.Add(window.onDidChangeVisibleTextEditors.Invoke(unbox textEditorsChangedHandler))
        disposables.Add(Notifications.onDocumentParsed.Invoke(unbox documentParsedHandler))
        disposables.Add(workspace.onDidCloseTextDocument.Invoke(unbox closedTextDocumentHandler))

        let newState =
            { decorationType = decorationType
              disposables = disposables
              documents = Documents.create () }

        state <- Some newState

        logger.Debug "Installed"

    let uninstall () =
        logger.Debug "Uninstalling"

        match state with
        | None -> ()
        | Some state ->
            for disposable in state.disposables do
                disposable.dispose () |> ignore

            state.decorationType.dispose ()

        state <- None
        logger.Debug "Uninstalled"

    let configChangedHandler (config: LineLensConfig ref) decorationType =
        logger.Debug("Config Changed event")

        let wasEnabled = (config.Value.enabled) && state <> None
        config.Value <- getConfig ()
        let isEnabled = config.Value.enabled

        if wasEnabled <> isEnabled then
            if isEnabled then install decorationType else uninstall ()

    member t.removeDocument(uri: Uri) =
        match state with
        | Some state ->
            let documentExistInCache =
                state.documents
                // Try to find the document in the cache
                // We use the path as the search value because parsed URI are not unified by VSCode
                |> Seq.tryFind (fun element -> element.Key.path = uri.path)

            match documentExistInCache with
            | Some(KeyValue(uri, _)) ->
                documentClosed uri state

                window.visibleTextEditors
                // Find the text editor related to the document in cache
                |> Seq.tryFind (fun textEditor -> textEditor.document.uri = uri)
                // If the text editor is found, remove the decorations
                |> Option.iter (fun textEditor ->
                    textEditor.setDecorations (state.decorationType, U2.Case1(ResizeArray())))

            | None -> ()

        | None -> ()



    member t.activate(context: ExtensionContext) =
        logger.Info "Activating"
        let changeHandler = fun () -> configChangedHandler (ref config) decorationType

        workspace.onDidChangeConfiguration $ (changeHandler, (), context.subscriptions)
        |> ignore

        changeHandler ()
        ()

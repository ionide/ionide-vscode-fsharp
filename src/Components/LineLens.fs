module Ionide.VSCode.FSharp.LineLens

open System
open System.Collections.Generic
open Fable.Core
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Fable.Core.JsInterop
open DTO
open Ionide.VSCode.Helpers

type Number = float

let private logger = ConsoleAndOutputChannelLogger(Some "LineLens", Level.DEBUG, None, Some Level.DEBUG)

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module LineLensConfig =

    open System.Text.RegularExpressions

    type EnabledMode =
        | Never
        | ReplaceCodeLens
        | Always

    let private parseEnabledMode (s : string) =
        match s.ToLowerInvariant() with
        | "never" -> Never
        | "always" -> Always
        | "replacecodelens"
        | _ -> ReplaceCodeLens

    type LineLensConfig =
        { enabled : EnabledMode
          prefix : string }

    let defaultConfig =
        { enabled = ReplaceCodeLens
          prefix = " //  " }

    let private themeRegex = Regex("\s*theme\((.+)\)\s*")

    let getConfig () =
        let cfg = workspace.getConfiguration()
        let fsharpCodeLensConfig = cfg.get("[fsharp]", JsObject.empty).tryGet<bool>("editor.codeLens")

        { enabled = cfg.get("FSharp.lineLens.enabled", "replacecodelens") |> parseEnabledMode
          prefix = cfg.get("FSharp.lineLens.prefix", defaultConfig.prefix) }

    let isEnabled conf =
        match conf.enabled with
        | Always -> true
        | ReplaceCodeLens -> true
        | _ -> false

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

    let inline set fileName value (documents : Documents) = documents.Add(fileName, value)

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

let mutable private config = LineLensConfig.defaultConfig

module LineLensDecorations =

    let create range text =
        // What we add after the range
        let attachment = createEmpty<ThemableDecorationAttachmentRenderOptions>
        attachment.color <- Some (U2.Case2 (ThemeColor "fsharp.linelens"))
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

    let formatSignature (sign : SignatureData) : string =
        let formatType =
            function
            | Contains "->" t -> sprintf "(%s)" t
            | t -> t

        let args =
            sign.Parameters
            |> List.map (fun group ->
                group
                |> List.map (fun p -> formatType p.Type)
                |> String.concat " * "
            )
            |> String.concat " -> "

        if String.IsNullOrEmpty args then sign.OutputType else args + " -> " + formatType sign.OutputType

    let interestingSymbolPositions (symbols : Symbols[]) : DTO.Range[] =
        symbols |> Array.collect(fun syms ->
            let interestingNested =
                syms.Nested
                |> Array.choose (fun sym ->
                    if sym.GlyphChar <> "Fc"
                       && sym.GlyphChar <> "M"
                       && sym.GlyphChar <> "F"
                       && sym.GlyphChar <> "P"
                       || sym.IsAbstract
                       || sym.EnclosingEntity = "I"  // interface
                       || sym.EnclosingEntity = "R"  // record
                       || sym.EnclosingEntity = "D"  // DU
                       || sym.EnclosingEntity = "En" // enum
                       || sym.EnclosingEntity = "E"  // exception
                    then None
                    else Some sym.BodyRange)

            if syms.Declaration.GlyphChar <> "Fc" then
                interestingNested
            else
                interestingNested |> Array.append [|syms.Declaration.BodyRange|])

    let private lineRange (doc : TextDocument) (range : DTO.Range) : CodeRange.CodeRange =
        let lineNumber = float range.StartLine - 1.
        let textLine = doc.lineAt lineNumber
        textLine.range

    let private getSignature (fileName : string) (range : DTO.Range) =
        promise {
            let! signaturesResult =
                LanguageService.signatureData
                    fileName
                    range.StartLine
                    (range.StartColumn - 1)
            let signaturesResult = if isNotNull signaturesResult then Some signaturesResult else None
            return signaturesResult |> Option.map (fun r -> range, formatSignature r.Data)
        }

    let private signatureToDecoration (doc : TextDocument) (range : DTO.Range, signature : string) =
        LineLensDecorations.create (lineRange doc range) (config.prefix + signature)

    let private onePerLine (ranges : Range[]) =
        ranges
        |> Array.groupBy(fun r -> r.StartLine)
        |> Array.choose (fun (_, ranges) -> if ranges.Length = 1 then Some (ranges.[0]) else None)

    let private needUpdate (fileName : string) (version : Number) { documents = documents }=
        (documents |> Documents.tryGetCachedAtVersion fileName version).IsSome

    let private declarationsResultToSignatures declarationsResult fileName =
        promise {
            let interesting = declarationsResult.Data |> interestingSymbolPositions
            let interesting = onePerLine interesting
            let! signatures = interesting |> Array.map (getSignature fileName) |> Promise.all
            return signatures |> Seq.choose id
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
                let text = document.getText()
                let! declarationsResult = LanguageService.lineLenses fileName
                if document.version = version && isNotNull declarationsResult then
                    let! signatures = declarationsResultToSignatures declarationsResult fileName
                    let info = state.documents |> Documents.getOrAdd fileName
                    if document.version = version && info.cache.IsNone || info.cache.Value.version <> version then
                        let decorations = signatures |> Seq.map (signatureToDecoration document) |> ResizeArray

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

    let decorationType = window.createTextEditorDecorationType(LineLensDecorations.decorationType)
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

    let wasEnabled = (LineLensConfig.isEnabled config) && state <> None
    config <- LineLensConfig.getConfig ()
    let isEnabled = LineLensConfig.isEnabled config

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

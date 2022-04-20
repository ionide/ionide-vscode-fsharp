module Ionide.VSCode.FSharp.InlayHints

open Fable.Core
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open global.Node
open Fable.Core.JsInterop

let private logger =
    ConsoleAndOutputChannelLogger(Some "InlayHints", Level.DEBUG, None, Some Level.DEBUG)

let enabled () : bool =
    Configuration.getUnsafe "FSharp.inlayHints.enabled"


let allowTypeAnnotations () : bool =
    Configuration.getUnsafe "FSharp.inlayHints.typeAnnotations"

let allowParameterNames () : bool =
    Configuration.getUnsafe "FSharp.inlayHints.parameterNames"

let actuallyEnabled () =
    enabled ()
    && (allowTypeAnnotations () || allowParameterNames ())

let inline createEdit (h: LanguageService.Types.InlayHint): TextEdit =
    let e = createEmpty<TextEdit>
    e.range <- vscode.Range.Create(h.pos, h.pos)
    e.newText <- h.text
    e

let toInlayHint (fsacHint: LanguageService.Types.InlayHint) : InlayHint =
    let h = createEmpty<InlayHint>
    h.position <- vscode.Position.Create(fsacHint.pos.line, fsacHint.pos.character)
    h.label <- U2.Case1 fsacHint.text

    match fsacHint.kind with
    | "Type" ->
        h.paddingLeft <- Some true
        h.kind <- Some InlayHintKind.Type
        h.textEdits <- Some (ResizeArray([ createEdit fsacHint ]))
    | "Parameter" ->
        h.paddingRight <- Some true
        h.kind <- Some InlayHintKind.Parameter
        // TODO: we don't easily create edits for parameter names - it might help if the insert text
        // was provided from FSAC as well (because FSAC knows if parens would be required, etc)
    | _ -> ()
    h

let inlayProvider () =
    let events = vscode.EventEmitter.Create()
    let mutable ev = events.event
    let disposables = ResizeArray()

    workspace.onDidChangeTextDocument.Invoke (fun e ->
        if e.document.languageId = "fsharp" then
            events.fire ()

        None)
    |> disposables.Add

    workspace.onDidChangeConfiguration.Invoke(
        (fun _ ->
            events.fire ()
            None)
    )
    |> disposables.Add

    { new InlayHintsProvider with
        member x.onDidChangeInlayHints = Some ev

        member x.provideInlayHints(document: TextDocument, range: Range, token: CancellationToken) =
            let doThing () =
                promise {
                    logger.Info("Getting inlay hints for %s", document.fileName)

                    let! hints =
                        LanguageService.inlayHints (
                            document.uri,
                            { start = range.start
                              ``end`` = range.``end`` }
                        )

                    let allowedHints =
                        match allowParameterNames (), allowTypeAnnotations () with
                        | true, true -> hints
                        | false, true -> hints |> Array.filter (fun h -> h.kind = "Type")
                        | true, false ->
                            hints
                            |> Array.filter (fun h -> h.kind = "Parameter")
                        | false, false -> [||] // not actually a thing, covered by the actuallyEnabled

                    return
                        allowedHints
                        |> Seq.map toInlayHint
                        |> ResizeArray
                        |> Some
                }
                |> Promise.toThenable

            if not (actuallyEnabled ()) then
                ProviderResult.None
            else
                ProviderResult.Some(U2.Case2(doThing ()))

        member x.resolveInlayHint(hint, token: CancellationToken) = None

        member x.onDidChangeInlayHints
            with set v = v |> Option.iter (fun v -> ev <- v) },
    disposables

let activate (context: ExtensionContext) =
    let provider, disposables = inlayProvider ()

    let selector =
        createObj [ "language" ==> "fsharp" ]
        |> unbox<DocumentFilter>

    languages.registerInlayHintsProvider (DocumentSelector.Case1 selector, provider)
    |> context.Subscribe

    disposables |> Seq.iter context.Subscribe

    logger.Info "Activating F# inlay hints"
    ()

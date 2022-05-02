module Ionide.VSCode.FSharp.InlayHints

open Fable.Core
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open global.Node
open Fable.Core.JsInterop

let private logger =
    ConsoleAndOutputChannelLogger(Some "InlayHints", Level.DEBUG, None, Some Level.DEBUG)

let mutable private toggleSupported = false

module Config =
    let enabled = "FSharp.inlayHints.enabled"
    let typeAnnotationsEnabled = "FSharp.inlayHints.typeAnnotations"
    let parameterNamesEnabled = "FSharp.inlayHints.parameterNames"
    let toggle = "editor.inlayHints.toggle"
    let disableLongTooltip = "FSharp.inlayHints.disableLongTooltip"

let enabled () : bool = Configuration.getUnsafe Config.enabled

let allowTypeAnnotations () : bool =
    Configuration.getUnsafe Config.typeAnnotationsEnabled

let allowParameterNames () : bool =
    Configuration.getUnsafe Config.parameterNamesEnabled

let actuallyEnabled () =
    enabled ()
    && (allowTypeAnnotations () || allowParameterNames ())

let isSetToToggle () =
    Configuration.tryGet Config.toggle
    |> Option.map (fun key -> key = "toggle")
    |> Option.defaultValue false

let useLongTooltip () : bool =
    Configuration.getUnsafe Config.disableLongTooltip
    |> Option.map not
    |> Option.defaultValue true

let inline createEdit (pos: Position, text: string): TextEdit =
    let e = createEmpty<TextEdit>
    e.range <- vscode.Range.Create(pos, pos)
    e.newText <- text
    e

module Commands =
    let hideTypeAnnotations = "fsharp.inlayHints.hideTypeAnnotations"
    let hideParameterNames = "fsharp.inlayHints.hideParameterNames"
    let hideAll = "fsharp.inlayHints.hideAll"
    let setToToggle = "fsharp.inlayHints.setToToggle"
    let disableLongTooltip = "fsharp.inlayHints.disableLongTooltip"

let toInlayHint useLongTooltip isSetToToggle (fsacHint: LanguageService.Types.InlayHint) : InlayHint =
    let h = createEmpty<InlayHint>
    h.position <- vscode.Position.Create(fsacHint.pos.line, fsacHint.pos.character)
    h.label <- U2.Case1 fsacHint.text

    let tip kind =
        if useLongTooltip then
            let lines = ResizeArray()

            let hideCommand =
                match kind with
                | LanguageService.Types.InlayHintKind.Type -> Commands.hideTypeAnnotations
                | LanguageService.Types.InlayHintKind.Parameter -> Commands.hideParameterNames

            lines.Add $"To hide these hints, [click here](command:{hideCommand})."
            lines.Add $"To hide *ALL* hints, [click here](command:{Commands.hideAll})."

            if not isSetToToggle && toggleSupported then
                lines.Add
                    $"Hints can also be hidden by default, and shown when Ctrl/Cmd+Alt is pressed. To do this, [click here](command:{Commands.setToToggle})."

            lines.Add
                $"Finally, to dismiss this long tooltip forever, [click here](command:{Commands.disableLongTooltip})."

            let t = vscode.MarkdownString.Create(String.concat " " lines)
            t.isTrusted <- Some true
            Some t
        else
            None

    match fsacHint.kind with
    | LanguageService.Types.InlayHintKind.Type ->
        h.paddingLeft <- Some true
        h.kind <- Some InlayHintKind.Type
    | LanguageService.Types.InlayHintKind.Parameter ->
        h.paddingRight <- Some true
        h.kind <- Some InlayHintKind.Parameter
    // TODO: we don't easily create edits for parameter names - it might help if the insert text
    // was provided from FSAC as well (because FSAC knows if parens would be required, etc)
    h.tooltip <- tip fsacHint.kind |> Option.map U2.Case2
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
                        | false, true ->
                            hints
                            |> Array.filter (fun h -> h.kind = LanguageService.Types.InlayHintKind.Type)
                        | true, false ->
                            hints
                            |> Array.filter (fun h -> h.kind = LanguageService.Types.InlayHintKind.Parameter)
                        | false, false -> [||] // not actually a thing, covered by the actuallyEnabled

                    let useLongTooltip = useLongTooltip ()
                    let isSetToToggle = toggleSupported && isSetToToggle ()

                    return
                        allowedHints
                        |> Seq.map (toInlayHint useLongTooltip isSetToToggle)
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

let supportsToggle (vscodeVersion: string) =
    let compareOptions = createEmpty<Semver.Options>
    compareOptions.includePrerelease <- Some true
    // toggle was introduced in 1.67.0, so any version of that should allow us to set the toggle
    Semver.semver.gte (U2.Case1 vscodeVersion, U2.Case1 "1.67.0", U2.Case2 compareOptions)

let activate (context: ExtensionContext) =
    let provider, disposables = inlayProvider ()
    toggleSupported <- supportsToggle vscode.version

    let selector =
        createObj [ "language" ==> "fsharp" ]
        |> unbox<DocumentFilter>

    commands.registerCommand (
        Commands.disableLongTooltip,
        (fun _ ->
            Configuration.set Config.disableLongTooltip (Some true)
            |> box
            |> Some)
    )
    |> context.Subscribe

    if toggleSupported then
        commands.registerCommand (
            Commands.setToToggle,
            (fun _ ->
                Configuration.set Config.toggle (Some "toggle")
                |> box
                |> Some)
        )
        |> context.Subscribe

    commands.registerCommand (
        Commands.hideAll,
        (fun _ ->
            Configuration.set Config.enabled (Some false)
            |> box
            |> Some)
    )
    |> context.Subscribe

    commands.registerCommand (
        Commands.hideParameterNames,
        (fun _ ->
            Configuration.set Config.parameterNamesEnabled (Some false)
            |> box
            |> Some)
    )
    |> context.Subscribe

    commands.registerCommand (
        Commands.hideTypeAnnotations,
        (fun _ ->
            Configuration.set Config.typeAnnotationsEnabled (Some false)
            |> box
            |> Some)
    )
    |> context.Subscribe

    languages.registerInlayHintsProvider (DocumentSelector.Case1 selector, provider)
    |> context.Subscribe

    disposables |> Seq.iter context.Subscribe

    logger.Info "Activating F# inlay hints"
    ()

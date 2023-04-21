namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.VSCode.Vscode
open Ionide.VSCode.Helpers
open DTO

module node = Node.Api

module InfoPanel =

    let private isFsharpTextEditor (textEditor: TextEditor) =
        if JS.isDefined textEditor && JS.isDefined textEditor.document then
            let doc = textEditor.document

            match doc with
            | Document.FSharp
            | Document.FSharpScript -> true
            | _ -> false
        else
            false

    module Panel =

        let showdownOptions =
            Fable.Import.Showdown.showdown.getDefaultOptions () :?> Fable.Import.Showdown.Showdown.ConverterOptions

        showdownOptions.tables <- Some true

        let showdown = Fable.Import.Showdown.showdown.Converter.Create(showdownOptions)

        let mutable panel: WebviewPanel option = None

        let mutable locked = false

        let setContent str =
            panel
            |> Option.iter (fun p ->
                let str = showdown.makeHtml str

                let str =
                    sprintf
                        """
                    <html>
                    <head>
                    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline';">
                    <style>
                    pre {
                      color: var(--vscode-editor-foreground);
                      font-family: var(--vscode-editor-font-family);
                      font-weight: var(--vscode-editor-font-weight);
                      font-size: var(--vscode-editor-font-size);
                    }
                    code {
                      font-family: var(--vscode-editor-font-family);
                      font-weight: var(--vscode-editor-font-weight);
                      font-size: var(--vscode-editor-font-size);
                    }
                    </style>
                    </head>
                    <body>
                    %s
                    </body>
                    </html>
                    """
                        str

                p.webview.html <- str)

        let clear () =
            panel |> Option.iter (fun p -> p.webview.html <- "")

        let mapContent res =
            if isNotNull res then
                let res: DocumentationDescription = res.Data

                let fsharpBlock lines =
                    let cnt = (lines |> String.concat "\n")

                    if String.IsNullOrWhiteSpace cnt then
                        ""
                    else
                        sprintf "<pre>\n%s\n</pre>" cnt

                let sigContent =
                    res.Signature
                    |> String.split [| '\n' |]
                    |> Array.filter (not << String.IsNullOrWhiteSpace)
                    |> fsharpBlock

                let commentContent = res.Comment

                let footerContent =
                    res.FooterLines
                    |> String.concat "\n\n"

                let ctors =
                    res.Constructors
                    |> List.filter (not << String.IsNullOrWhiteSpace)
                    |> List.distinct
                    |> fsharpBlock

                let intfs =
                    res.Interfaces
                    |> List.filter (not << String.IsNullOrWhiteSpace)
                    |> List.distinct
                    |> List.sort
                    |> fsharpBlock

                let attrs =
                    res.Attributes
                    |> List.filter (not << String.IsNullOrWhiteSpace)
                    |> List.distinct
                    |> List.sort
                    |> fsharpBlock

                let fncs =
                    res.Functions
                    |> List.filter (not << String.IsNullOrWhiteSpace)
                    |> List.distinct
                    |> fsharpBlock

                let fields =
                    res.Fields
                    |> List.filter (not << String.IsNullOrWhiteSpace)
                    |> List.distinct
                    |> fsharpBlock

                let types =
                    res.DeclaredTypes
                    |> List.filter (not << String.IsNullOrWhiteSpace)
                    |> List.distinct
                    |> fsharpBlock

                let res =
                    [| yield sigContent
                       if not (String.IsNullOrWhiteSpace commentContent) then
                           yield "---"
                           yield commentContent
                           yield "\n"
                       if not (String.IsNullOrWhiteSpace types) then
                           yield "---"
                           yield "#### Declared Types"
                           yield types
                           yield "\n"
                       if not (String.IsNullOrWhiteSpace attrs) then
                           yield "---"
                           yield "#### Attributes"
                           yield attrs
                           yield "\n"
                       if not (String.IsNullOrWhiteSpace intfs) then
                           yield "---"
                           yield "#### Implemented Interfaces"
                           yield intfs
                           yield "\n"
                       if not (String.IsNullOrWhiteSpace ctors) then
                           yield "---"
                           yield "#### Constructors"
                           yield ctors
                           yield "\n"
                       if not (String.IsNullOrWhiteSpace fncs) then
                           yield "---"
                           yield "#### Functions"
                           yield fncs
                           yield "\n"
                       if not (String.IsNullOrWhiteSpace fields) then
                           yield "---"
                           yield "#### Fields"
                           yield fields
                           yield "\n"
                       if not (String.IsNullOrWhiteSpace footerContent) then
                           yield "---"
                           yield (footerContent)

                       |]
                    |> String.concat "\n"

                Some res
            else
                None

        let update (textEditor: TextEditor) (selections: ResizeArray<Selection>) =
            promise {
                if isFsharpTextEditor textEditor && selections.Count > 0 && panel.IsSome then
                    let doc = textEditor.document
                    let pos = selections.[0].active
                    let! res = LanguageService.documentation doc.uri (int pos.line) (int pos.character)
                    res |> Option.bind mapContent |> Option.iter setContent
                else
                    return ()
            }
            |> ignore

        let update' (textEditor: TextEditor) (pos: Position) =
            promise {
                if isFsharpTextEditor textEditor && panel.IsSome then
                    let doc = textEditor.document
                    let! res = LanguageService.documentation doc.uri (int pos.line) (int pos.character)
                    res |> Option.bind mapContent |> Option.iter setContent
                else
                    return ()
            }
            |> ignore

        let updateOnLink xmlSig assemblyName =
            promise {
                let! res = LanguageService.documentationForSymbol xmlSig assemblyName
                res |> Option.bind mapContent |> Option.iter setContent
            }

    let mutable private timer = None

    let private clearTimer () =
        match timer with
        | Some t ->
            clearTimeout t
            timer <- None
        | _ -> ()

    let private openPanel () =
        promise {
            match Panel.panel with
            | Some p -> p.reveal (!! -2, true)
            | None ->
                let opts =
                    createObj
                        [ "enableCommandUris" ==> true
                          "enableFindWidget" ==> true
                          "retainContextWhenHidden" ==> true ]

                let viewOpts = createObj [ "preserveFocus" ==> true; "viewColumn" ==> -2 ]

                let p = window.createWebviewPanel ("infoPanel", "Info Panel", !!viewOpts, opts)

                let onChange (event: WebviewPanelOnDidChangeViewStateEvent) =
                    Context.set "infoPanelFocused" event.webviewPanel.active

                let onClose () =
                    clearTimer ()
                    Panel.panel <- None

                p.onDidChangeViewState.Invoke(!!onChange) |> ignore

                p.onDidDispose.Invoke(!!onClose) |> ignore
                Panel.panel <- Some p
                let textEditor = window.activeTextEditor.Value
                let selection = textEditor.selections

                do
                    if not Panel.locked then
                        Panel.update textEditor selection
        }

    let private updatePanel () =
        match Panel.panel with
        | Some _ ->
            let textEditor = window.activeTextEditor.Value
            let selection = textEditor.selections
            Panel.update textEditor selection
        | None -> openPanel () |> ignore

    let private showDocumentation o =
        // If the panel doesn't exist, open it
        // This happens when using click on "Open documentation" from inside
        // the tooltip
        promise {
            match Panel.panel with
            | Some _ -> ()
            | None ->
                do! openPanel ()

            do! Panel.updateOnLink !!o?XmlDocSig !!o?AssemblyName
        }

    let private selectionChanged (event: TextEditorSelectionChangeEvent) =
        let updateMode = "FSharp.infoPanelUpdate" |> Configuration.get "onCursorMove"

        if not Panel.locked && (updateMode = "onCursorMove" || updateMode = "both") then
            clearTimer ()
            timer <- Some(setTimeout (fun () -> Panel.update event.textEditor event.selections) 500.)


    let private documentParsedHandler (event: Notifications.DocumentParsedEvent) =
        if event.document = window.activeTextEditor.Value.document && not Panel.locked then
            clearTimer ()
            Panel.update window.activeTextEditor.Value window.activeTextEditor.Value.selections

        ()

    let tooltipRequested (pos: Position) =
        let updateMode = "FSharp.infoPanelUpdate" |> Configuration.get "onCursorMove"

        if updateMode = "onHover" || updateMode = "both" then
            clearTimer ()
            timer <- Some(setTimeout (fun () -> Panel.update' window.activeTextEditor.Value pos) 500.)


    let lockPanel () =
        Panel.locked <- true
        Context.set "infoPanelLocked" true
        ()

    let unlockPanel () =
        Panel.locked <- false
        Context.set "infoPanelLocked" false
        ()



    let activate (context: ExtensionContext) =
        let startLocked = "FSharp.infoPanelStartLocked" |> Configuration.get false

        let show = "FSharp.infoPanelShowOnStartup" |> Configuration.get false

        context.Subscribe(window.onDidChangeTextEditorSelection.Invoke(selectionChanged >> box >> Some))
        context.Subscribe(Notifications.onDocumentParsed.Invoke(documentParsedHandler >> box >> Some))
        context.Subscribe(Notifications.tooltipRequested.Invoke(tooltipRequested >> box >> Some))

        commands.registerCommand ("fsharp.openInfoPanel", openPanel |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsharp.updateInfoPanel", updatePanel |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsharp.openInfoPanel.lock", lockPanel |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsharp.openInfoPanel.unlock", unlockPanel |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsharp.showDocumentation", showDocumentation |> objfy2)
        |> context.Subscribe

        if startLocked then
            Panel.locked <- true

        if show && window.visibleTextEditors |> Seq.exists isFsharpTextEditor then
            openPanel () |> ignore
        else
            ()

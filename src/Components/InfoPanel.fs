namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers
open DTO
open Fable
open Fable.Import.vscode
module node = Fable.Import.Node.Exports

module InfoPanel =

    module Panel =

        let showdown = Fable.Import.Showdown.showdown.Converter.Create()

        let mutable panel : WebviewPanel option = None

        let mutable locked = false

        let private isFsharpTextEditor (textEditor : TextEditor) =
            if JS.isDefined textEditor && JS.isDefined textEditor.document then
                let doc = textEditor.document
                match doc with
                | Document.FSharp -> true
                | _ -> false
            else
                false


        let setContent str =
            panel |> Option.iter (fun p ->
                let str = showdown.makeHtml str
                let str =
                    sprintf """
                    <html>
                    <head>
                    <meta http-equiv="Content-Security-Policy" content="default-src 'none';">
                    <style>
                    pre {color: var(--textCodeBlock.background)}
                    </style>
                    </head>
                    <body>
                    %s
                    </body>
                    </html>
                    """ str

                p.webview.html <- str
            )

        let clear () = panel |> Option.iter (fun p -> p.webview.html <- "")

        let mapContent res =
            if isNotNull res then
                let res : DocumentationDescription = (res.Data |> Array.concat).[0]

                let fsharpBlock lines =
                    let cnt = (lines |> String.concat "\n")
                    if String.IsNullOrWhiteSpace cnt then ""
                    else sprintf "<pre>\n%s\n</pre>" cnt

                let sigContent =
                    let lines =
                        res.Signature
                        |> String.split [|'\n'|]
                        |> Array.filter (not << String.IsNullOrWhiteSpace)

                    match lines |> Array.splitAt (lines.Length - 1) with
                    | (h, [| StartsWith "Full name:" fullName |]) ->
                        [| yield fsharpBlock h
                           yield "*" + fullName + "*" |]
                    | _ -> [| fsharpBlock lines |]
                    |> String.concat "\n"

                let commentContent =
                    res.Comment
                    |> Markdown.createCommentString

                let footerContent =
                    res.Footer
                    |> String.split [|'\n' |]
                    |> Array.filter (not << String.IsNullOrWhiteSpace)
                    |> Array.map (fun n -> "*" + n + "*")
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
                    res.Types
                    |> List.filter (not << String.IsNullOrWhiteSpace)
                    |> List.distinct
                    |> fsharpBlock

                let res =
                    [|
                        yield sigContent
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

                    |] |> String.concat "\n"
                Some res
            else
                None

        let update (textEditor : TextEditor) (selections : ResizeArray<Selection>) =
            promise {
                if isFsharpTextEditor textEditor && selections.Count > 0 then
                    let doc = textEditor.document
                    let pos = selections.[0].active
                    let! res = LanguageService.documentation doc.fileName (int pos.line) (int pos.character)
                    let res = mapContent res
                    match res with
                    | None -> ()
                    | Some res ->
                        setContent res
                else
                    return ()
            } |> ignore

        let update' (textEditor : TextEditor) (pos : Position) =
            promise {
                if isFsharpTextEditor textEditor  then
                    let doc = textEditor.document
                    let! res = LanguageService.documentation doc.fileName (int pos.line) (int pos.character)
                    let res = mapContent res
                    match res with
                    | None -> ()
                    | Some res ->
                        setContent res
                else
                    return ()
            } |> ignore

        let updateOnLink xmlSig assemblyName =
            promise {
                let! res = LanguageService.documentationForSymbol xmlSig assemblyName
                let res = mapContent res
                match res with
                | None -> ()
                | Some res ->
                    setContent res
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
            | Some p ->
                p.reveal (!! -2, true)
            | None ->
                let opts =
                    createObj [
                        "enableCommandUris" ==> true
                        "enableFindWidget" ==> true
                        "retainContextWhenHidden" ==> true
                    ]
                let viewOpts =
                    createObj [
                        "preserveFocus" ==> true
                        "viewColumn" ==> -2
                    ]
                let p = window.createWebviewPanel("infoPanel", "Info Panel", !!viewOpts , opts)
                let onChange (event : WebviewPanelOnDidChangeViewStateEvent) =
                    Context.set "infoPanelFocused" event.webviewPanel.active

                let onClose () =
                    clearTimer ()
                    Panel.panel <- None

                p.onDidChangeViewState.Invoke(!!onChange) |> ignore
                p.onDidDispose.Invoke(!!onClose) |> ignore
                Panel.panel <- Some p
                let textEditor = window.activeTextEditor
                let selection = window.activeTextEditor.selections
                do if not Panel.locked then Panel.update textEditor selection
        }

    let private updatePanel () =
        match Panel.panel with
        | Some _ ->
            let textEditor = window.activeTextEditor
            let selection = window.activeTextEditor.selections
            Panel.update textEditor selection
        | None ->
            openPanel () |> ignore

    let private showDocumentation o =
        Panel.updateOnLink !!o?XmlDocSig !!o?AssemblyName


    let private selectionChanged (event : TextEditorSelectionChangeEvent) =
        let updateMode = "FSharp.infoPanelUpdate" |> Configuration.get "onCursorMove"

        if not Panel.locked && (updateMode = "onCursorMove" || updateMode = "both") then
            clearTimer()
            timer <- Some (setTimeout (fun () -> Panel.update event.textEditor event.selections) 500.)


    let private documentParsedHandler (event : Notifications.DocumentParsedEvent) =
        if event.document = window.activeTextEditor.document && not Panel.locked then
            clearTimer()
            Panel.update window.activeTextEditor window.activeTextEditor.selections
        ()

    let tooltipRequested (pos: Position) =
        let updateMode = "FSharp.infoPanelUpdate" |> Configuration.get "onCursorMove"
        if updateMode = "onHover" || updateMode = "both" then
            clearTimer()
            timer <- Some (setTimeout (fun () ->Panel.update' window.activeTextEditor pos) 500.)


    let lockPanel () =
        Panel.locked <- true
        Context.set "infoPanelLocked" true
        ()

    let unlockPanel () =
        Panel.locked <- false
        Context.set "infoPanelLocked" false
        ()



    let activate (context : ExtensionContext) =
        let startLocked = "FSharp.infoPanelStartLocked" |> Configuration.get false
        let show = "FSharp.infoPanelShowOnStartup" |> Configuration.get false

        context.subscriptions.Add(window.onDidChangeTextEditorSelection.Invoke(unbox selectionChanged))
        context.subscriptions.Add(Notifications.onDocumentParsed.Invoke(unbox documentParsedHandler))
        context.subscriptions.Add(Notifications.tooltipRequested.Invoke(!! tooltipRequested))

        commands.registerCommand("fsharp.openInfoPanel", openPanel |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsharp.updateInfoPanel", updatePanel |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsharp.openInfoPanel.lock", lockPanel |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsharp.openInfoPanel.unlock", unlockPanel |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsharp.showDocumentation", showDocumentation |> unbox<Func<obj,obj>>) |> context.subscriptions.Add

        if startLocked then Panel.locked <- true
        if show then openPanel () |> ignore
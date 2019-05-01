namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers
open DTO
module node = Fable.Import.Node.Exports

module InfoPanel =

    module Panel =

        let showdown = Fable.Import.Showdown.showdown.Converter.Create()

        let mutable panel : WebviewPanel option = None

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
                    <style>
                    pre {color: var(--textCodeBlock.background)}
                    </style>
                    </head>
                    <body>
                    %s
                    </body>
                    </html>
                    """ str

                printf "TEST: %s" str
                p.webview.html <- str
            )

        let clear () = panel |> Option.iter (fun p -> p.webview.html <- "")

        let mapContent res =
            if isNotNull res then
                let res = (res.Data |> Array.concat).[0]

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

                let res =
                    [|
                        yield sigContent
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
                        if not (String.IsNullOrWhiteSpace commentContent) then
                            yield "---"
                            yield commentContent
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
                    let! res = LanguageService.documentation doc.fileName (int pos.line + 1) (int pos.character + 1)
                    let res = mapContent res
                    match res with
                    | None -> ()
                    | Some res ->
                        setContent res
                else
                    return ()
            } |> ignore

        let updateOnClick xmlSig assemblyName =
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
            let opts =
                createObj [
                    "enableCommandUris" ==> true
                ]
            let p = window.createWebviewPanel("infoPanel", "Info Panel", !!9 , opts)
            Panel.panel <- Some p
            return ()
        }

    let private showDocumentation o =
        Panel.updateOnClick !!o?XmlDocSig !!o?AssemblyName


    let private selectionChanged (event : TextEditorSelectionChangeEvent) =
        clearTimer()
        timer <- Some (setTimeout (fun () -> Panel.update event.textEditor event.selections) 500.)

    let private textEditorChanged (_textEditor : TextEditor) =
        clearTimer()
        // The display is always cleared, if it's an F# document an onDocumentParsed event will arrive
        //Panel.clear()

    let private documentParsedHandler (event : Errors.DocumentParsedEvent) =
        if event.document = window.activeTextEditor.document then
            clearTimer()
            Panel.update window.activeTextEditor window.activeTextEditor.selections
        ()



    let activate (context : ExtensionContext) =

        context.subscriptions.Add(window.onDidChangeTextEditorSelection.Invoke(unbox selectionChanged))
        context.subscriptions.Add(window.onDidChangeActiveTextEditor.Invoke(unbox textEditorChanged))
        context.subscriptions.Add(Errors.onDocumentParsed.Invoke(unbox documentParsedHandler))
        commands.registerCommand("fsharp.openInfoPanel", openPanel |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsharp.showDocumentation", showDocumentation |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
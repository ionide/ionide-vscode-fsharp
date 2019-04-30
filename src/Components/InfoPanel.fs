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
                p.webview.html <- str
            )

        let clear () = panel |> Option.iter (fun p -> p.webview.html <- "")

        let update (textEditor : TextEditor) (selections : ResizeArray<Selection>) =
            promise {
                if isFsharpTextEditor textEditor && selections.Count > 0 then
                    let doc = textEditor.document
                    let pos = selections.[0].active
                    let! res = LanguageService.tooltip doc.fileName (int pos.line + 1) (int pos.character + 1)
                    let range = doc.getWordRangeAtPosition pos
                    if isNotNull res then
                        let res = (res.Data |> Array.concat).[0]

                        let fsharpBlock (lines: string[]) =
                            (lines |> String.concat "\n") |> sprintf "```fsharp\n%s\n```"

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

                        let commentContent =
                            res.Comment
                            |> Markdown.createCommentString

                        let footerContent =
                            res.Footer
                            |> String.split [|'\n' |]
                            |> Array.filter (not << String.IsNullOrWhiteSpace)
                            |> Array.map (fun n -> "*" + n + "*")


                        let res =
                            [|
                                yield! sigContent
                                yield "---"
                                yield commentContent
                                yield "\n"
                                yield "---"
                                yield (footerContent |> String.concat "\n\n")

                            |] |> String.concat "\n"
                        setContent res

                        ()
                else
                    return ()
            } |> ignore

    let mutable private timer = None

    let private clearTimer () =
        match timer with
        | Some t ->
            clearTimeout t
            timer <- None
        | _ -> ()

    let private openPanel () =
        promise {
            let p = window.createWebviewPanel("infoPanel", "Info Panel", !!9 )
            Panel.panel <- Some p
            return ()
        }


    let private selectionChanged (event : TextEditorSelectionChangeEvent) =
        clearTimer()
        timer <- Some (setTimeout (fun () -> Panel.update event.textEditor event.selections) 500.)

    let private textEditorChanged (_textEditor : TextEditor) =
        clearTimer()
        // The display is always cleared, if it's an F# document an onDocumentParsed event will arrive
        Panel.clear()

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
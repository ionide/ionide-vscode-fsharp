namespace Ionide.VSCode.FSharp

open Fable.Import
open Fable.Import.vscode
open Ionide.VSCode.Helpers

module QuickInfo =

    module private StatusDisplay =

        let mutable private item : StatusBarItem option = None
        let private hideItem () = item |> Option.iter (fun n -> n.hide ())

        let private showItem (text : string) tooltip =
            item.Value.text <- text
            item.Value.tooltip <- tooltip
            item.Value.show()

        let activate (context : ExtensionContext) =
            item <- Some (window.createStatusBarItem (unbox 1, -10. ))
            context.subscriptions.Add(item.Value)

        let private isFsharpTextEditor (textEditor : TextEditor) =
            if JS.isDefined textEditor && JS.isDefined textEditor.document then
                let doc = textEditor.document
                match doc with
                | Document.FSharp -> true
                | _ -> false
            else
                false

        let private getOverloadSignature (textEditor : TextEditor) (selections : ResizeArray<Selection>) =
            promise {
                if isFsharpTextEditor textEditor && selections.Count > 0 then
                    let doc = textEditor.document
                    let pos = selections.[0].active
                    let! o = LanguageService.signature (doc.fileName) (int pos.line + 1) (int pos.character + 1)
                    if isNotNull o then
                        return Some o.Data
                    else
                        return None
                else
                    return None
            }

        let update (textEditor : TextEditor) (selections : ResizeArray<Selection>) =
            promise {
                let! signature = getOverloadSignature textEditor selections
                match signature with
                | Some signature ->
                    showItem signature signature
                | _ ->
                    hideItem()
            } |> ignore

        let clear () =
            update JS.undefined (ResizeArray())

    let mutable private timer = None
    let private clearTimer () =
        match timer with
        | Some t ->
            clearTimeout t
            timer <- None
        | _ -> ()

    let private selectionChanged (event : TextEditorSelectionChangeEvent) =
        clearTimer()
        timer <- Some (setTimeout (fun () -> StatusDisplay.update event.textEditor event.selections) 500.)

    let private textEditorChanged (_textEditor : TextEditor) =
        clearTimer()
        // The display is always cleared, if it's an F# document an onDocumentParsed event will arrive
        StatusDisplay.clear()

    let private documentParsedHandler (event : Errors.DocumentParsedEvent) =
        if event.document = window.activeTextEditor.document then
            clearTimer()
            StatusDisplay.update window.activeTextEditor window.activeTextEditor.selections
        ()


    let activate (context : ExtensionContext) =
        StatusDisplay.activate context

        context.subscriptions.Add(window.onDidChangeTextEditorSelection.Invoke(unbox selectionChanged))
        context.subscriptions.Add(window.onDidChangeActiveTextEditor.Invoke(unbox textEditorChanged))
        context.subscriptions.Add(Errors.onDocumentParsed.Invoke(unbox documentParsedHandler))
module Ionide.VSCode.FSharp.QuickInfo

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers

module private StatusDisplay =
    let mutable private item : StatusBarItem option = None
    let private hideItem () = item |> Option.iter (fun n -> n.hide ())

    let private showItem (text: string) tooltip =
        item.Value.text <- text
        item.Value.tooltip <- tooltip
        item.Value.show()

    let activate (context: ExtensionContext) =
        item <- Some (window.createStatusBarItem (unbox 1, -10. ))
        context.subscriptions.Add(item.Value)

    let private getOverloadSignature (textEditor : TextEditor) (selections: ResizeArray<Selection>) = promise {
        if JS.isDefined textEditor?document then
            let doc = textEditor.document
            match doc with
            | Document.FSharp ->
                let pos = selections.[0].active
                let! o = LanguageService.signature (doc.fileName) (int pos.line + 1) (int pos.character + 1)
                if isNotNull o then
                    return Some o.Data
                else
                    return None
            | _ -> return None
        else
            return None
    }

    let update (textEditor : TextEditor) (selections: ResizeArray<Selection>) =
        promise {
            let! signature = getOverloadSignature textEditor selections
            match signature with
            | Some signature ->
                showItem signature signature
            | _ ->
                hideItem()
        } |> ignore

    let clear () = update JS.undefined (ResizeArray())

let mutable private timer = None
let private clearTimer () = timer |> Option.iter(clearTimeout)

let private selectionChanged (event : TextEditorSelectionChangeEvent) =
    clearTimer()
    timer <- Some (setTimeout (fun () -> StatusDisplay.update event.textEditor event.selections) 500.)

let private documentClosed (document: TextDocument) =
    clearTimer()
    StatusDisplay.clear()

let private documentParsedHandler (event: Errors.DocumentParsedEvent) =
    if event.document = window.activeTextEditor.document then
        clearTimer()
        StatusDisplay.update window.activeTextEditor window.activeTextEditor.selections
    ()

let activate (context: ExtensionContext) =
    StatusDisplay.activate context

    context.subscriptions.Add(window.onDidChangeTextEditorSelection.Invoke(unbox selectionChanged))
    context.subscriptions.Add(workspace.onDidCloseTextDocument.Invoke(unbox documentClosed))
    context.subscriptions.Add(Errors.onDocumentParsed.Invoke(unbox documentParsedHandler))
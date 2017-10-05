module Ionide.VSCode.FSharp.QuickInfo

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers

let private logger = ConsoleAndOutputChannelLogger(Some "QuickInfo", Level.DEBUG, None, Some Level.DEBUG)

let mutable private item : StatusBarItem option = None
let hideItem () = item |> Option.iter (fun n -> n.hide ())

let showItem (text: string) tooltip =
    item.Value.text <- text
    item.Value.tooltip <- tooltip
    item.Value.show()

let private getCurrentOverloadSignature (textEditor : TextEditor) (selections: ResizeArray<Selection>) = promise {
    if JS.isDefined textEditor?document then
        let doc = textEditor.document
        match doc with
        | Document.FSharp ->
            let pos = selections.[0].active
            let! o = LanguageService.tooltip (doc.fileName) (int pos.line + 1) (int pos.character + 1)
            if isNotNull o then
                let signature = (o.Data |> Array.collect id).[0]
                if JS.isDefined signature.Signature then
                    return Some signature
                else
                    return None
            else
                return None
        | _ -> return None
    else
        return None
}

let private handle' (event : TextEditorSelectionChangeEvent) =
    promise {
        logger.Debug("Changed")
        let! signature = getCurrentOverloadSignature event.textEditor event.selections
        logger.Debug("Signature", signature)
        match signature with
        | Some signature ->
            let t = signature.Signature.Split('\n').[0]
            showItem t signature.Signature
        | _ ->
            hideItem()
    }

let mutable private timer = None
let clearTimer () = timer |> Option.iter(clearTimeout)

let private selectionChanged (event : TextEditorSelectionChangeEvent) =
    clearTimer()
    timer <- Some (setTimeout (fun n -> handle' event |> ignore) 500.)

let private documentClosed (document: TextDocument) =
    clearTimer()
    hideItem()

let activate (context: ExtensionContext) =
    logger.Debug("Activating")
    item <- Some (window.createStatusBarItem ())
    context.subscriptions.Add(item.Value)

    context.subscriptions.Add(window.onDidChangeTextEditorSelection.Invoke(unbox selectionChanged))
    context.subscriptions.Add(workspace.onDidCloseTextDocument.Invoke(unbox documentClosed))

    logger.Debug("Activated")
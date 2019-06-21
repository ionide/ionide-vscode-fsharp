namespace Ionide.VSCode.FSharp

open Fable.Import
open Fable.Import.vscode
open Ionide.VSCode.Helpers

module Fsdn =

    open System
    open Fable.Core
    open Fable.Core.JsInterop

    let pickSignature (functions: string list) =

        let text (x : string) =
            let item = createEmpty<QuickPickItem>
            item.label <- x
            item.description <- sprintf "Signature: %s" x
            item

        match functions |> List.map (fun x -> (text x), x) with
        | [] ->
            None |> Promise.lift
        | projects ->
            promise {
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Signatures"
                let chooseFrom = projects |> List.map fst |> ResizeArray
                let! chosen = window.showQuickPick(chooseFrom |> U2.Case1, opts)
                if JS.isDefined chosen then
                    let selected = projects |> List.tryFind (fun (qp, _) -> qp = chosen) |> Option.map snd
                    match selected with
                    | Some selected ->
                        return Some selected
                    | None -> return None
                else
                    return None
            }

    let private query () =
        promise {

            let opts = createEmpty<InputBoxOptions>
            opts.prompt <- Some "Signature"
            let! signature =  window.showInputBox(opts)

            let! ws = LanguageService.fsdn signature
            return ws.Functions |> List.ofArray
        }

    let activate (context : ExtensionContext) =

        commands.registerCommand("fsharp.fsdn", (fun _ ->

            let docUri = window.activeTextEditor.document.uri

            let activeSelection = window.activeTextEditor.selection.active
            let line = activeSelection.line
            let col = activeSelection.character

            query ()
            |> Promise.bind pickSignature
            |> Promise.bind (fun functionName ->
                match functionName with
                | None -> Promise.lift false
                | Some name ->
                    let edit = WorkspaceEdit()
                    edit.insert(docUri, Position((line, col)), name)
                    workspace.applyEdit edit
                )
            |> box
            ))
        |> context.subscriptions.Add

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
                    let! o = LanguageService.signature (doc.fileName) (int pos.line) (int pos.character)
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

    let private documentParsedHandler (event : Notifications.DocumentParsedEvent) =
        if event.document = window.activeTextEditor.document then
            clearTimer()
            StatusDisplay.update window.activeTextEditor window.activeTextEditor.selections
        ()


    let activate (context : ExtensionContext) =
        StatusDisplay.activate context

        context.subscriptions.Add(window.onDidChangeTextEditorSelection.Invoke(unbox selectionChanged))
        context.subscriptions.Add(window.onDidChangeActiveTextEditor.Invoke(unbox textEditorChanged))
        context.subscriptions.Add(Notifications.onDocumentParsed.Invoke(unbox documentParsedHandler))
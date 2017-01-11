namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers


module QuickInfo =
    let mutable private item : StatusBarItem option = None

    let private handle' (event : TextEditorSelectionChangeEvent) =
        promise {
            if JS.isDefined event.textEditor?document then
                let doc = event.textEditor.document
                match doc with
                | Document.FSharp ->
                    let pos = event.selections.[0].active
                    let! o = LanguageService.tooltip (doc.fileName) (int pos.line + 1) (int pos.character + 1)
                    if isNotNull o then
                        let res = (o.Data |> Array.collect id).[0].Signature
                        if JS.isDefined res then
                            let t = res.Split('\n').[0]
                            item |> Option.iter (fun n -> n.hide ())
                            let i = window.createStatusBarItem (1 |> unbox, -1.)
                            i.text <- t
                            i.tooltip <- res
                            i.show ()
                            item <- Some i
                | _ -> ()
        }

    let mutable private timer = None

    let private handle (event : TextEditorSelectionChangeEvent) =
        timer |> Option.iter(clearTimeout)
        timer <- Some (setTimeout((fun n -> handle' event), 500.) )

    let activate (disposables: Disposable[]) =
        window.onDidChangeTextEditorSelection $ (handle, (), disposables) |> ignore
        ()
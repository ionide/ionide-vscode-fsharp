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
    [<Emit("setTimeout($0,$1)")>]
    let setTimeout(cb, delay) : obj = failwith "JS Only"

    [<Emit("clearTimeout($0)")>]
    let clearTimeout(timer) : unit = failwith "JS Only"

    let mutable private item : StatusBarItem option = None

    let private handle' (event : TextEditorSelectionChangeEvent) =
        promise {
            if JS.isDefined event.textEditor?document then
                let doc = event.textEditor.document
                let pos = event.selections.[0].active
                let! o = LanguageService.tooltip (doc.fileName) (int pos.line + 1) (int pos.character + 1)
                if o |> unbox <> null then
                    let res = (o.Data |> Array.fold (fun acc n -> (n |> Array.toList) @ acc ) []).Head.Signature
                    if JS.isDefined res then
                        let t = res.Split('\n').[0]
                        item |> Option.iter (fun n -> n.hide ())
                        let i = window.createStatusBarItem (1 |> unbox, -1.)
                        i.text <- t
                        i.tooltip <- res
                        i.show ()
                        item <- Some i
        }

    let mutable private timer = None

    let private handle (event : TextEditorSelectionChangeEvent) =
        timer |> Option.iter(clearTimeout)
        timer <- Some (setTimeout((fun n -> handle' event), 500.) )

    let activate (disposables: Disposable[]) =
        window.onDidChangeTextEditorSelection $ (handle, (), disposables) |> ignore
        ()
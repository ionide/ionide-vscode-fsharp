namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers

module Fsi =
    let mutable fsiProcess : child_process_types.ChildProcess option = None
    let mutable fsiOutput : OutputChannel option =
        window.createOutputChannel("F# Interactive")
        |> Some

    let private handle (data : obj) =
        if data <> null then
            let response = data.ToString().Replace("\\","\\\\")
            fsiOutput |> Option.iter (fun outChannel -> outChannel.append response |> ignore)

    let private start () =
        try
            // window.showInformationMessage ("FSI path: " + Environment.fsi) |> ignore
            fsiProcess |> Option.iter(fun fp -> fp.kill ())
            fsiProcess <-
                (Process.spawn Environment.fsi "" "--fsi-server-input-codepage:65001")
                |> Process.onExit (fun _ -> fsiOutput |> Option.iter (fun outChannel -> outChannel.clear () ))
                |> Process.onOutput handle
                |> Process.onError handle
                |> Some
            fsiOutput |> Option.iter (fun outChannel -> outChannel.show (2 |> unbox) )
        with
        | _ ->
            window.showErrorMessage "Failed to spawn FSI, please ensure it's in PATH" |> ignore


    let private send (msg : string) file =

        if fsiProcess.IsNone then start ()
        let msg = msg + "\n;;\n"
        fsiOutput |> Option.iter (fun outChannel -> outChannel.append msg)
        let msg' =
            try
                let dir = path.dirname file
                "\n"
                + (sprintf "# silentCd @\"%s\" ;; " dir) + "\n"
                + (sprintf "# %d @\"%s\" " 1 file) + "\n"
                + msg
            with
            | _ -> msg

        fsiProcess |> Option.iter (fun fp -> fp.stdin.write(msg', "utf-8" |> unbox) |> ignore)


    let private sendLine () =
        let editor = window.activeTextEditor
        let file = editor.document.fileName
        let pos = editor.selection.start
        let line = editor.document.lineAt pos
        send line.text file
        commands.executeCommand "cursorDown" |> ignore

    let private sendSelection () =
        let editor = window.activeTextEditor
        let file = editor.document.fileName

        if editor.selection.isEmpty then
            sendLine ()
        else
            let range = Range(editor.selection.anchor.line, editor.selection.anchor.character, editor.selection.active.line, editor.selection.active.character)
            let text = editor.document.getText range
            send text file

    let private sendFile () =
        let editor = window.activeTextEditor
        let file = editor.document.fileName
        let text = editor.document.getText ()
        send text file



    let activate (disposables: Disposable[]) =
        commands.registerCommand("fsi.Start", start |> unbox) |> ignore
        commands.registerCommand("fsi.SendLine", sendLine |> unbox) |> ignore
        commands.registerCommand("fsi.SendSelection", sendSelection |> unbox) |> ignore
        commands.registerCommand("fsi.SendFile", sendFile |> unbox) |> ignore


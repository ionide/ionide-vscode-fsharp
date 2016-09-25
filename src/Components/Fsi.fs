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
    let mutable fsiOutput : Terminal option = None

    let isPowershell () =
        let t = workspace.getConfiguration().get("terminal.integrated.shell.windows", "")
        t.ToLower().Contains "powershell"

    let private start () =
        try
            fsiOutput |> Option.iter (fun n -> n.dispose())
            let terminal = window.createTerminal("F# Interactive")
            fsiOutput <- Some terminal
            let fsi, clear =
                if Environment.isWin then
                    if isPowershell () then
                        sprintf "cmd /c \"%s\" --fsi-server-input-codepage:65001" Environment.fsi, "clear"
                    else
                        sprintf "\"%s\" --fsi-server-input-codepage:65001" Environment.fsi, "cls"
                else
                    Environment.fsi, "clear"
            terminal.sendText(clear, true)
            terminal.sendText(fsi, true)
            terminal.show(true)
        with
        | _ ->
            window.showErrorMessage "Failed to spawn FSI, please ensure it's in PATH" |> ignore


    let private send (msg : string) file =

        if fsiOutput.IsNone then start ()
        let msg = msg + ";;\n"
        let msg' =
            try
                let dir = path.dirname file
                "\n"
                + (sprintf "# silentCd @\"%s\" ;; " dir) + "\n"
                + (sprintf "# %d @\"%s\" " 1 file) + "\n"
                + msg
            with
            | _ -> msg

        fsiOutput |> Option.iter (fun fp -> fp.sendText(msg,false) )


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


namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.child_process

open DTO
open Ionide.VSCode.Helpers

[<ReflectedDefinition>]
module Fsi =
    let mutable fsiProcess : ChildProcess option = None
    let mutable fsiOutput : OutputChannel option = None

    let private handle (data : obj) =
        if data <> null then
            let response = data.ToString().Replace("\\","\\\\")
            fsiOutput |> Option.iter (fun outChannel -> outChannel.append response |> ignore)

    let private start () =
        try
            fsiProcess |> Option.iter(fun fp -> fp.kill ())
            fsiProcess <-
                (if Process.isWin () then Process.spawn "Fsi.exe" "" "--fsi-server-input-codepage:65001" else Process.spawn "fsharpi" "" "--fsi-server-input-codepage:65001")
                |> Process.onExit (fun _ -> fsiOutput |> Option.iter (fun outChannel -> outChannel.clear () ))
                |> Process.onOutput handle
                |> Process.onError handle
                |> Some
            fsiOutput <-
                window.Globals.createOutputChannel("F# Interactive")
                |> Some
            fsiOutput |> Option.iter (fun outChannel -> outChannel.show (2 |> unbox) )
        with
        | _ ->
            window.Globals.showErrorMessage "Failed to spawn FSI, please ensure it's in PATH" |> ignore
            

    let private send (msg : string) file =

        if fsiProcess.IsNone then start ()
        let msg = msg + ";;\n"
        fsiOutput |> Option.iter (fun outChannel -> outChannel.append msg)
        let msg' =
            try
                let dir = path.Globals.dirname file
                "\n"
                + (sprintf "# silentCd @\"%s\" ;; " dir) + "\n"
                + (sprintf "# %d @\"%s\" " 1 file) + "\n"
                + msg
            with
            | _ -> msg

        fsiProcess |> Option.iter (fun fp -> fp.stdin.write(msg', "utf-8" |> unbox) |> ignore)

    let private sendLine () =
        let editor = window.Globals.activeTextEditor
        let file = editor.document.fileName
        let pos = editor.selection.start
        let line = editor.document.lineAt pos
        send line.text file

    let private sendSelection () =
        let editor = window.Globals.activeTextEditor
        let file = editor.document.fileName
        let range = Range.Create (editor.selection.anchor, editor.selection.active)
        let text = editor.document.getText range
        send text file

    let private sendFile () =
        let editor = window.Globals.activeTextEditor
        let file = editor.document.fileName
        let text = editor.document.getText ()
        send text file

    

    let activate (disposables: Disposable[]) =
        commands.Globals.registerCommand("fsi.Start", start |> unbox) |> ignore
        commands.Globals.registerCommand("fsi.SendLine", sendLine |> unbox) |> ignore
        commands.Globals.registerCommand("fsi.SendSelection", sendSelection |> unbox) |> ignore
        commands.Globals.registerCommand("fsi.SendFile", sendFile |> unbox) |> ignore


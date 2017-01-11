namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers

module LegacyFsi =
    let mutable fsiProcess : child_process_types.ChildProcess option = None
    let mutable fsiOutput : OutputChannel option =
        window.createOutputChannel("F# Interactive")
        |> Some

    let private handle (data : obj) =
        if isNotNull data then
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
                |> Process.onErrorOutput handle
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

    let private referenceAssembly (path:ProjectReferencePath) = path |> sprintf "#r @\"%s\"" |> fun m -> send m null

    let private sendReferences () =
        window.activeTextEditor.document.fileName
        |> Project.tryFindLoadedProjectByFile
        |> Option.iter (fun p -> p.References |> List.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not ) |>  List.iter referenceAssembly )

    let private generateProjectReferences () =
        let ctn =
            window.activeTextEditor.document.fileName
            |> Project.tryFindLoadedProjectByFile
            |> Option.map (fun p ->
                [|
                    yield! p.References |> List.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not ) |> List.map (sprintf "#r @\"%s\"")
                    yield! p.Files |> List.map (sprintf "#load @\"%s\"")
                |])
        promise {
            match ctn with
            | Some c ->
                let path = path.join(workspace.rootPath, "references.fsx")
                let! td = vscode.Uri.parse ("untitled:" + path) |> workspace.openTextDocument
                let! te = window.showTextDocument(td, ViewColumn.Three)
                let! res = te.edit (fun e ->
                    let p = Position(0.,0.)
                    let ctn = c |> String.concat "\n"
                    e.insert(p,ctn))


                return ()
            | None ->
                return ()
        }

    let activate (disposables: Disposable[]) =
        commands.registerCommand("fsi.Start", start |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsi.SendLine", sendLine |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsi.SendSelection", sendSelection |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsi.SendFile", sendFile |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsi.SendProjectReferences", sendReferences |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsi.GenerateProjectReferences", generateProjectReferences |> unbox<Func<obj,obj>>) |> ignore

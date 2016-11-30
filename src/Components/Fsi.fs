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
    let mutable fsiOutputPID : int option = None
    let mutable lastSelectionSent : string option = None

    let isPowershell () =
        let t = "terminal.integrated.shell.windows" |> Configuration.get ""
        t.ToLower().Contains "powershell"

    let sendCd () =
        let editor = window.activeTextEditor
        let file,dir =
            if  JS.isDefined editor then
                let file = editor.document.fileName
                let dir = path.dirname file
                file,dir
            else
                let dir = workspace.rootPath
                path.join(dir, "tmp.fsx"), dir
        let msg1 = sprintf "# silentCd @\"%s\";;\n" dir
        let msg2 = (sprintf "# %d @\"%s\" \n" 1 file)


        fsiOutput |> Option.iter (fun n ->
            n.sendText(msg1, false)
            n.sendText(msg2, false)
            n.sendText(";;\n", false)
        )

    let private start () =
        promise {
            fsiOutput |> Option.iter (fun n -> n.dispose())
            let parms =
                let fsiParams =
                    "FSharp.fsiExtraParameters"
                    |> Configuration.get Array.empty<string>
                    |> List.ofArray

                if Environment.isWin then
                    [ "--fsi-server-input-codepage:65001" ] @ fsiParams
                else
                    fsiParams
                |> Array.ofList

            let terminal = window.createTerminal("F# Interactive", Environment.fsi, parms)
            terminal.processId |> Promise.onSuccess (fun pId -> fsiOutputPID <- Some pId) |> ignore

            fsiOutput <- Some terminal
            sendCd ()
            terminal.show(true)
            return terminal

            }
        |> Promise.onFail (fun _ ->
            window.showErrorMessage "Failed to spawn FSI, please ensure it's in PATH" |> ignore)


    let private send (msg : string) =
        let msgWithNewline = msg + "\n;;\n"
        match fsiOutput with
        | None -> start ()
        | Some fo -> Promise.lift fo
        |> Promise.onSuccess (fun fp ->
            fp.show true
            fp.sendText(msgWithNewline,false)
            lastSelectionSent <- Some msg)
        |> Promise.onFail (fun error ->
            window.showErrorMessage "Failed to send text to FSI" |> ignore)

    let private sendLine () =
        let editor = window.activeTextEditor
        let file = editor.document.fileName
        let pos = editor.selection.start
        let line = editor.document.lineAt pos
        send line.text
        |> Promise.onSuccess (fun _ -> commands.executeCommand "cursorDown" |> ignore)
        |> Promise.suppress // prevent unhandled promise exception
        |> ignore

    let private sendSelection () =
        let editor = window.activeTextEditor
        let file = editor.document.fileName

        if editor.selection.isEmpty then
            sendLine ()
        else
            let range = Range(editor.selection.anchor.line, editor.selection.anchor.character, editor.selection.active.line, editor.selection.active.character)
            let text = editor.document.getText range
            send text
            |> Promise.suppress // prevent unhandled promise exception
            |> ignore

    let private sendLastSelection () =
        match lastSelectionSent with
        | Some x ->
            if "FSharp.saveOnSendLastSelection" |> Configuration.get false then
                window.activeTextEditor.document.save ()
            else
                Promise.lift true
            |> Promise.bind(fun _ ->
                send x)
            |> Promise.suppress // prevent unhandled promise exception
            |> ignore
        | None -> ()

    let private sendFile () =
        let editor = window.activeTextEditor
        let text = editor.document.getText ()
        send text
        |> Promise.suppress // prevent unhandled promise exception
        |> ignore

    let private referenceAssembly (path:ProjectReferencePath) = path |> sprintf "#r @\"%s\"" |> send
    let private referenceAssemblies = Promise.executeForAll referenceAssembly

    let private sendReferences () =
        window.activeTextEditor.document.fileName
        |> Project.tryFindLoadedProjectByFile
        |> Option.iter (fun p -> p.References  |> List.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not )  |> referenceAssemblies |> Promise.suppress |> ignore)

    let private handleCloseTerminal (terminal:Terminal) =
        fsiOutputPID
        |> Option.iter (fun currentTerminalPID ->
            terminal.processId
            |> Promise.onSuccess (fun closedTerminalPID ->
                if closedTerminalPID = currentTerminalPID then
                    fsiOutput <- None
                    fsiOutputPID <- None)
            |> Promise.suppress // prevent unhandled promise exception
            |> ignore)
        |> ignore

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
        window.onDidChangeActiveTextEditor $ ((fun n -> if JS.isDefined n then sendCd()), (), disposables) |> ignore
        window.onDidCloseTerminal $ (handleCloseTerminal, (), disposables) |> ignore

        commands.registerCommand("fsi.Start", start |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsi.SendLine", sendLine |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsi.SendSelection", sendSelection |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsi.SendLastSelection", sendLastSelection |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsi.SendFile", sendFile |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsi.SendProjectReferences", sendReferences |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsi.GenerateProjectReferences", generateProjectReferences |> unbox<Func<obj,obj>>) |> ignore


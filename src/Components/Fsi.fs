namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers
module node = Fable.Import.Node.Exports

module Fsi =
    let mutable fsiOutput : Terminal option = None
    let mutable fsiOutputPID : int option = None
    let mutable lastSelectionSent : string option = None

    let mutable lastCd : string option = None
    let mutable lastCurrentFile : string option = None

    let sendCd (textEditor : TextEditor) =
        let file, dir =
            if JS.isDefined textEditor then
                let file = textEditor.document.fileName
                let dir = node.path.dirname file
                file, dir
            else
                let dir = workspace.rootPath
                node.path.join(dir, "tmp.fsx"), dir

        match lastCd with
        | Some(cd) when cd = dir -> ()
        | _ ->
            let msg = sprintf "# silentCd @\"%s\";;\n" dir
            fsiOutput |> Option.iter (fun n -> n.sendText(msg, false))
            lastCd <- Some dir

        match lastCurrentFile with
        | Some (currentFile) when currentFile = file -> ()
        | _ ->
            let msg = sprintf "# %d @\"%s\"\n;;\n" 1 file
            fsiOutput |> Option.iter (fun n -> n.sendText(msg, false))
            lastCurrentFile <- Some file

    let private start () =
        promise {
            fsiOutput |> Option.iter (fun n -> n.dispose())
            let isSdk =
                "FSharp.useSdkScripts"
                |> Configuration.get false


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
            let! fsiPath =
                LanguageService.fsi ()
                |> Promise.bind (fun fsi -> match fsi with Some fsi -> Promise.lift fsi | None -> Promise.reject "FSI not found")

            let! dotnet = LanguageService.dotnet()

            let terminal =
                if isSdk && dotnet.IsSome then
                    window.createTerminal("F# Interactive (.Net Core)", dotnet.Value, [|yield "fsi"; yield! parms |])
                else
                    window.createTerminal("F# Interactive", fsiPath, parms)
            terminal.processId |> Promise.onSuccess (fun pId -> fsiOutputPID <- Some pId) |> ignore
            lastCd <- None
            lastCurrentFile <- None
            fsiOutput <- Some terminal
            sendCd window.activeTextEditor
            terminal.show(true)
            return terminal
        }
        |> Promise.onFail (fun _ ->
            window.showErrorMessage "Failed to spawn FSI, please ensure it's in PATH" |> ignore)

    let private chunkStringBySize (size : int) (str : string) =
        let mutable i1 = 0
        [while i1 < str.Length do
            let i2 = min (i1 + size) (str.Length)
            yield str.[i1..i2-1]
            i1 <- i2]

    let private send (msg : string) =
        let msgWithNewline = msg + (if msg.Contains "//" then "\n" else "") + ";;\n"
        match fsiOutput with
        | None -> start ()
        | Some fo -> Promise.lift fo
        |> Promise.onSuccess (fun fp ->
            fp.show true

            //send in chunks of 256, terminal has a character limit
            msgWithNewline
            |> chunkStringBySize 256
            |> List.iter (fun x -> fp.sendText(x,false))

            lastSelectionSent <- Some msg)
        |> Promise.onFail (fun _ ->
            window.showErrorMessage "Failed to send text to FSI" |> ignore)

    let private sendLine () =
        let editor = window.activeTextEditor
        let _ = editor.document.fileName
        let pos = editor.selection.start
        let line = editor.document.lineAt pos
        sendCd editor
        send line.text
        |> Promise.onSuccess (fun _ -> commands.executeCommand "cursorDown" |> ignore)
        |> Promise.suppress // prevent unhandled promise exception
        |> ignore

    let private sendSelection () =
        let editor = window.activeTextEditor
        let _ = editor.document.fileName

        sendCd editor
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
        sendCd editor
        send text
        |> Promise.suppress // prevent unhandled promise exception
        |> ignore

    let private sendText (lines : string list) =
        send (String.concat ";;\n" lines)
        |> Promise.suppress // prevent unhandled promise exception
        |> ignore

    let private referenceAssembly (path : ProjectReferencePath) = path |> sprintf "#r @\"%s\"" |> send
    let private referenceAssemblies = Promise.executeForAll referenceAssembly

    let private sendReferences () =
        window.activeTextEditor.document.fileName
        |> Project.tryFindLoadedProjectByFile
        |> Option.iter (fun p ->
            sendCd window.activeTextEditor
            p.References
            |> List.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not )
            |> referenceAssemblies
            |> Promise.suppress
            |> ignore)

    let private handleCloseTerminal (terminal : Terminal) =
        fsiOutputPID
        |> Option.iter (fun currentTerminalPID ->
            terminal.processId
            |> Promise.onSuccess (fun closedTerminalPID ->
                if closedTerminalPID = currentTerminalPID then
                    fsiOutput <- None
                    fsiOutputPID <- None
                    lastCd <- None
                    lastCurrentFile <- None)
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
                let path = node.path.join(workspace.rootPath, "references.fsx")
                let! td = Uri.parse ("untitled:" + path) |> workspace.openTextDocument
                let! te = window.showTextDocument(td, ViewColumn.Three)
                let! _ = te.edit (fun e ->
                    let p = Position(0.,0.)
                    let ctn = c |> String.concat "\n"
                    e.insert(p,ctn))


                return ()
            | None ->
                return ()
        }

    let sendReferencesForProject project =
        project.References  |> List.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not )  |> referenceAssemblies |> Promise.suppress |> ignore

    let generateProjectReferencesForProject project =
        let ctn =
            [|
                yield! project.References |> List.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not ) |> List.map (sprintf "#r @\"%s\"")
                yield! project.Files |> List.map (sprintf "#load @\"%s\"")
            |]
        promise {
            let path = node.path.join(workspace.rootPath, "references.fsx")
            let! td = Uri.parse ("untitled:" + path) |> workspace.openTextDocument
            let! te = window.showTextDocument(td, ViewColumn.Three)
            let! _ = te.edit (fun e ->
                let p = Position(0.,0.)
                let ctn = ctn |> String.concat "\n"
                e.insert(p,ctn))
            return () }


    let activate (context : ExtensionContext) =
        window.onDidCloseTerminal $ (handleCloseTerminal, (), context.subscriptions) |> ignore

        commands.registerCommand("fsi.Start", start |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsi.SendLine", sendLine |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsi.SendSelection", sendSelection |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsi.SendLastSelection", sendLastSelection |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsi.SendFile", sendFile |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsi.SendText", sendText |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsi.SendProjectReferences", sendReferences |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsi.GenerateProjectReferences", generateProjectReferences |> unbox<Func<obj,obj>>) |> context.subscriptions.Add

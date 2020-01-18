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

    module Watcher =
        let mutable panel : WebviewPanel option = None

        let setContent str =
            panel |> Option.iter (fun p ->
                let str =
                    sprintf """
                    <html>
                    <head>
                    <meta>
                    <style>
                    table { border-collapse: collapse;}
                    th {
                        border-left: 1px solid var(--vscode-editor-foreground);
                        border-right: 1px solid var(--vscode-editor-foreground);
                        padding: 5px;
                    }
                    td {
                        border: 1px solid var(--vscode-editor-foreground);
                        min-width: 100px;
                        padding: 5px;
                    }
                    </style>
                    </head>
                    <body>
                    %s
                    </body>
                    </html>
                    """ str

                p.webview.html <- str
            )

        let openPanel () =
            promise {
                match panel with
                | Some p ->
                    p.reveal (!! -2, true)
                | None ->
                    let opts =
                        createObj [
                            "enableCommandUris" ==> true
                            "enableFindWidget" ==> true
                            "retainContextWhenHidden" ==> true
                        ]
                    let viewOpts =
                        createObj [
                            "preserveFocus" ==> true
                            "viewColumn" ==> -2
                        ]
                    let p = window.createWebviewPanel("fsiWatcher", "FSI Watcher", !!viewOpts , opts)
                    let onClose () =
                        panel <- None

                    p.onDidDispose.Invoke(!!onClose) |> ignore
                    panel <- Some p
            }

        let handler (uri: string) =
            node.fs.readFile(uri, (fun _ buf ->
                let cnt = buf.toString()
                cnt
                |> String.split [| '\n' |]
                |> Seq.map (fun row ->
                    let x = row.Split([|',' |])
                    sprintf "<tr><td>%s</td><td>%s</td><td>%s</td></tr>" x.[0] x.[1] x.[2]
                )
                |> String.concat "\n"
                |> sprintf "<table><tr><th>Name</th><th>Value</th><th>Type</th></tr>%s</table>"
                |> setContent
            ))

        let activate dispsables =
            let p = path.join(VSCodeExtension.ionidePluginPath (), "watcher", "vars.txt")
            fs.watchFile (p, (fun st st2 ->
                handler p
            ))




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

    let fsiBinaryAndParameters () =
        let isSdk =
            "FSharp.useSdkScripts"
            |> Configuration.get false

        let addWatcher =
            // "FSharp.addFsiWatcher"
            // |> Configuration.get false
            true

        let parms =
            let fsiParams =
                "FSharp.fsiExtraParameters"
                |> Configuration.get Array.empty<string>
                |> List.ofArray

            let p = path.join(VSCodeExtension.ionidePluginPath (), "watcher", "watcher.fsx")

            let fsiParams =
                if addWatcher then
                    [ "--load:" + p] @ fsiParams
                else
                    fsiParams

            if Environment.isWin then
                [ "--fsi-server-input-codepage:65001" ] @ fsiParams
            else
                fsiParams
            |> Array.ofList


        promise {
            if isSdk
            then
                let! dotnet = LanguageService.dotnet ()
                match dotnet with
                | Some dotnet ->
                    return dotnet, [|yield "fsi"; yield! parms |]
                | None ->
                    return failwith "dotnet fsi requested but no dotnet SDK was found."
            else
                let! fsi = LanguageService.fsi ()
                match fsi with
                | Some fsi ->
                    return fsi, parms
                | None ->
                    return failwith ".Net Framework FSI was requested but not found"
        }

    let private start () =
        fsiOutput |> Option.iter (fun n -> n.dispose())
        let isSdk =
            "FSharp.useSdkScripts"
            |> Configuration.get false

        promise {
            let! (fsiBinary, fsiArguments) = fsiBinaryAndParameters ()

            let terminal =
                if isSdk
                then
                    window.createTerminal("F# Interactive (.Net Core)", fsiBinary, fsiArguments)
                else
                    window.createTerminal("F# Interactive", fsiBinary, fsiArguments)

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
            fp.sendText(msgWithNewline, false)
            lastSelectionSent <- Some msg
        )
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
        Watcher.activate(!!context.subscriptions)
        window.onDidCloseTerminal $ (handleCloseTerminal, (), context.subscriptions) |> ignore

        commands.registerCommand("fsi.Start", start |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsi.SendLine", sendLine |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsi.SendSelection", sendSelection |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsi.SendLastSelection", sendLastSelection |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsi.SendFile", sendFile |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsi.SendText", sendText |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsi.SendProjectReferences", sendReferences |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsi.GenerateProjectReferences", generateProjectReferences |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsi.OpenWatcher", Watcher.openPanel |> unbox<Func<obj,obj>>) |> context.subscriptions.Add

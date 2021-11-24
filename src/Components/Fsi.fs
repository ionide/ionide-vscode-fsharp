namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open global.Node

open DTO
open Ionide.VSCode.Helpers
module node = Node.Api

module Fsi =
    module SdkScriptsNotify =

        open Ionide.VSCode.FSharp

        let suggestKey = "FSharp.suggestSdkScripts"
        let useKey = "FSharp.useSdkScripts"

        let shouldNotifyAboutSdkScripts () =
            let k = Configuration.get true useKey
            not k


        let disablePromptGlobally () =
            Configuration.setGlobal suggestKey (Some (box false))

        let disablePromptForProject () =
            Configuration.set suggestKey (Some (box false))

        let setUseSdk () =
            Configuration.setGlobal useKey (Some (box true))


        let checkForPatternsAndPromptUser () = promise {
            if shouldNotifyAboutSdkScripts () then
                let! choice = window.showInformationMessage("You are running .Net Core version of FsAutoComplete, we recommend also using .Net Core version of F# REPL (`dotnet fsi`). Should we change your settings (`FSharp.useSdkScripts`). This requires .Net Core 3.X?", "Update settings", "Ignore", "Don't show again")
                match choice with
                | Some "Update settings" ->
                    do! setUseSdk ()
                | Some "Ignore" ->
                    do! disablePromptForProject ()
                | Some "Don't show again" ->
                    do! disablePromptGlobally ()
                | _ -> ()
        }

        let activate (_context: ExtensionContext) =
            if Configuration.get true suggestKey
            then checkForPatternsAndPromptUser () |> ignore
            else ()

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
                let addWatcher =
                    "FSharp.addFsiWatcher"
                    |> Configuration.get false
                if not addWatcher then
                    let! res = window.showInformationMessage("FSI Watcher is an experimental feature, and it needs to be enabled with `FSharp.addFsiWatcher` setting", "Enable", "Ignore")
                    if res = Some "Enable" then
                        do! Configuration.setGlobal "FSharp.addFsiWatcher" (Some (box true))
                else
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

        let varsUri = path.join(VSCodeExtension.ionidePluginPath (), "watcher", "vars.txt")
        let typesUri = path.join(VSCodeExtension.ionidePluginPath (), "watcher", "types.txt")
        let funcUri = path.join(VSCodeExtension.ionidePluginPath (), "watcher", "funcs.txt")


        let handler () =
            let mutable varsContent = ""
            let mutable typesContent = ""
            let mutable funcsContent = ""


            node.fs.readFile(varsUri, (fun _ buf ->
                if not (Utils.isUndefined buf) then
                    let cnt = buf.ToString()
                    varsContent <-
                        cnt
                        |> String.split [| '\n' |]
                        |> Seq.map (fun row ->
                            let x = row.Split([|"###IONIDESEP###"|], StringSplitOptions.None)
                            sprintf "<tr><td>%s</td><td><code>%s</code></td><td><code>%s</code></td></tr>" x.[0] x.[1] x.[2]
                        )
                        |> String.concat "\n"
                        |> sprintf """</br><h3>Declared values</h3></br><table style="width:100%%"><tr><th style="width: 12%%">Name</th><th style="width: 65%%">Value</th><th style="width: 20%%">Type</th></tr>%s</table>"""


                    setContent (varsContent + "\n\n" + funcsContent + "\n\n" + typesContent)
            ))

            node.fs.readFile(funcUri, (fun _ buf ->
                if not (Utils.isUndefined buf) then
                    let cnt = buf.ToString()
                    funcsContent <-
                        cnt
                        |> String.split [| '\n' |]
                        |> Seq.map (fun row ->
                            let x = row.Split([|"###IONIDESEP###"|], StringSplitOptions.None)
                            sprintf "<tr><td>%s</td><td><code>%s</code></td><td><code>%s</code></td></tr>" x.[0] x.[1] x.[2]
                        )
                        |> String.concat "\n"
                        |> sprintf """</br><h3>Declared functions</h3></br><table style="width:100%%"><tr><th style="width: 12%%">Name</th><th style="width: 65%%">Parameters</th><th style="width: 20%%">Returned type</th></tr>%s</table>"""


                    setContent (varsContent + "\n\n" + funcsContent + "\n\n" + typesContent)
            ))

            node.fs.readFile(typesUri, (fun _ buf ->
                if not (Utils.isUndefined buf) then
                    let cnt = buf.ToString()
                    typesContent <-
                        if String.IsNullOrWhiteSpace cnt then
                            ""
                        else
                            cnt
                            |> String.split [| '\n' |]
                            |> Seq.map (fun row ->
                                let x = row.Split([|"###IONIDESEP###"|], StringSplitOptions.None)
                                let signature =
                                    if x.[1].Contains "#|#" then
                                    "| " + x.[1].Replace("#|#", "</br>| ")
                                    else x.[1]
                                sprintf "<tr><td>%s</td><td><code>%s</code></td></tr>" x.[0] signature
                            )
                            |> String.concat "\n"
                            |> sprintf """</br><h3>Declared types</h3></br><table style="width:100%%"><tr><th style="width: 12%%">Name</th><th style="width: 85%%">Signature</th></tr>%s</table>"""


                    setContent (varsContent + "\n\n" + funcsContent + "\n\n" + typesContent)
            ))

        let activate dispsables =
            fs.watchFile (varsUri, (fun st st2 ->
                handler ()
            ))
            fs.watchFile (typesUri, (fun st st2 ->
                handler ()
            ))
            fs.watchFile (funcUri, (fun st st2 ->
                handler ()
            ))

    let mutable fsiOutput : Terminal option = None
    let mutable fsiOutputPID : int option = None
    let mutable lastSelectionSent : string option = None

    let mutable lastCd : string option = None
    let mutable lastCurrentFile : string option = None

    let isSdk () =
        Configuration.get false "FSharp.useSdkScripts"

    let sendCd (textEditor : TextEditor option) =
        let file, dir =
            match textEditor with
            | Some textEditor ->
                let file = textEditor.document.fileName
                let dir = node.path.dirname file
                file, dir
            | None ->
                let dir = workspace.rootPath.Value
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
        let addWatcher =
            "FSharp.addFsiWatcher"
            |> Configuration.get false

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
                // these flags are added to work around issues with the vscode terminal shell on windows
                [ "--fsi-server-input-codepage:28591"
                  "--fsi-server-output-codepage:65001" ] @ fsiParams
            else
                fsiParams
            |> Array.ofList

        promise {
            if isSdk ()
            then
                let! dotnet = LanguageService.dotnet ()
                match dotnet with
                | Some dotnet ->
                    let! fsiSetting = LanguageService.fsiSdk ()
                    let fsiArg = defaultArg fsiSetting "fsi"
                    return dotnet, [|yield fsiArg; yield! parms |]
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

    let fsiNetCoreName = "F# Interactive (.Net Core)"
    let fsiNetFrameworkName = "F# Interactive"

    let provider: TerminalProfileProvider =
        { new TerminalProfileProvider with
            override this.provideTerminalProfile(token: CancellationToken): ProviderResult<TerminalProfile> =
                let work =
                    promise {
                        let! (fsiBinary, fsiArguments) = fsiBinaryAndParameters ()
                        let fsiArguments = U2.Case1 (ResizeArray fsiArguments)
                        let name =
                            if isSdk ()
                            then fsiNetCoreName
                            else fsiNetFrameworkName

                        let options: TerminalOptions = createEmpty<_>
                        options.name <- Some name
                        options.shellArgs <- Some fsiArguments
                        options.shellPath <- Some fsiBinary
                        let profile : TerminalProfile = createEmpty<_>
                        profile.options <- U2.Case1 options
                        return Some profile
                    }
                    |> Promise.toThenable
                Some (U2.Case2 work)
            }

    let private setupTerminalState (terminal: Terminal) =
        terminal.processId
        |> Promise.ofThenable
        |> Promise.onSuccess (fun pid -> fsiOutputPID <- Option.map int pid)
        |> ignore
        lastCd <- None
        lastCurrentFile <- None
        fsiOutput <- Some terminal

    let tryFindExistingTerminal () =
        window.terminals
        |> Seq.tryFind (fun t ->
            t.name = fsiNetFrameworkName || t.name = fsiNetCoreName
        )

    let private start () =
        promise {
            let ctok = vscode.CancellationTokenSource.Create().token
            let! profile =
                match provider.provideTerminalProfile(ctok) with
                | None -> promise.Return None
                | Some (U2.Case1 options) -> promise.Return (Some options)
                | Some (U2.Case2 work) -> Promise.ofThenable work
            let profile =
                match profile with
                | Some opts -> opts
                | None ->
                    window.showErrorMessage("Unable to spawn FSI", null) |> ignore
                    failwith "unable to spawn FSI"

            let terminal =
                match profile.options with
                | U2.Case1 opts -> window.createTerminal opts
                | U2.Case2 opts -> window.createTerminal opts

            terminal.show(true)
            return terminal
        }
        |> Promise.onFail (fun _ ->
            window.showErrorMessage("Failed to spawn FSI, please ensure it's in PATH", null) |> ignore
        )

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
            window.showErrorMessage("Failed to send text to FSI", null) |> ignore
        )

    let private moveCursorDownOneLine() =
        let args =
            createObj [
                "to" ==> "down"
                "by" ==> "line"
                "value" ==> 1
            ]
        commands.executeCommand("cursorMove", Some(box args))
        |> ignore

    let private sendLine () =
        let editor = window.activeTextEditor.Value
        let _ = editor.document.fileName
        let pos = editor.selection.start
        let line = editor.document.lineAt pos
        sendCd (Some editor)
        send line.text
        |> Promise.onSuccess (fun _ -> moveCursorDownOneLine())
        |> Promise.suppress // prevent unhandled promise exception
        |> ignore

    let private sendSelection () =
        let editor = window.activeTextEditor.Value
        let _ = editor.document.fileName

        sendCd (Some editor)
        if editor.selection.isEmpty then
            sendLine ()
        else
            let range = vscode.Range.Create(editor.selection.anchor.line, editor.selection.anchor.character, editor.selection.active.line, editor.selection.active.character)
            let text = editor.document.getText range
            send text
            |> Promise.suppress // prevent unhandled promise exception
            |> ignore

    let private sendLastSelection () =
        match lastSelectionSent with
        | Some x ->
            if "FSharp.saveOnSendLastSelection" |> Configuration.get false then
                window.activeTextEditor.Value.document.save () |> Promise.ofThenable
            else
                Promise.lift true
            |> Promise.bind(fun _ ->
                send x)
            |> Promise.suppress // prevent unhandled promise exception
            |> ignore
        | None -> ()

    let private sendFile () =
        let editor = window.activeTextEditor.Value
        let text = editor.document.getText ()
        sendCd (Some editor)
        send text
        |> Promise.suppress // prevent unhandled promise exception
        |> ignore

    let private sendText (lines : string list) =
        send (String.concat ";;\n" lines)
        |> Promise.suppress // prevent unhandled promise exception
        |> ignore

    let private referenceAssembly (path : ResolvedReferencePath) = path |> sprintf "#r @\"%s\"" |> send
    let private referenceAssemblies = Promise.executeForAll referenceAssembly

    let private sendReferences () =
        window.activeTextEditor.Value.document.fileName
        |> Project.tryFindLoadedProjectByFile
        |> Option.iter (fun p ->
            sendCd window.activeTextEditor
            p.References
            |> Seq.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not )
            |> Seq.toList
            |> referenceAssemblies
            |> Promise.suppress
            |> ignore)


    let private clearOldTerminalState () =
        fsiOutput |> Option.iter (fun t -> t.dispose())

    let private handleCloseTerminal (terminal : Terminal) =
        fsiOutputPID
        |> Option.iter (fun currentTerminalPID ->
            terminal.processId
            |> Promise.ofThenable
            |> Promise.onSuccess (fun closedTerminalPID ->
                if Option.map int closedTerminalPID = Some currentTerminalPID then
                    fsiOutput <- None
                    fsiOutputPID <- None
                    lastCd <- None
                    lastCurrentFile <- None)
            |> Promise.suppress // prevent unhandled promise exception
            |> ignore)
        |> ignore
        None

    // when a new terminal is created, if it's FSI and if we don't already have a terminal then setup the state for tracking FSI
    let private handleOpenTerminal (terminal: Terminal) =
        if terminal.name = fsiNetCoreName || terminal.name = fsiNetFrameworkName
        then
            clearOldTerminalState ()
            setupTerminalState terminal
            // initially have to set up the terminal to be in the correct start directory
            sendCd window.activeTextEditor

        None


    let private generateProjectReferences () =
        let ctn =
            window.activeTextEditor.Value.document.fileName
            |> Project.tryFindLoadedProjectByFile
            |> Option.map (fun p ->
                [|
                    yield! p.References |> Seq.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not ) |> Seq.map (sprintf "#r @\"%s\"")
                    yield! p.Files |> Seq.map (sprintf "#load @\"%s\"")
                |])
        promise {
            match ctn with
            | Some c ->
                let path = node.path.join(workspace.rootPath.Value, "references.fsx")
                let! td = vscode.Uri.parse ("untitled:" + path) |> workspace.openTextDocument
                let! te = window.showTextDocument(td, ViewColumn.Three)
                let! _ = te.edit (fun e ->
                    let p = vscode.Position.Create(0.,0.)
                    let ctn = c |> String.concat "\n"
                    e.insert(p,ctn))


                return ()
            | None ->
                return ()
        }

    let sendReferencesForProject project =
        project.References  |> Seq.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not ) |> Seq.toList |> referenceAssemblies |> Promise.suppress |> ignore

    let generateProjectReferencesForProject project =
        let ctn =
            [|
                yield! project.References |> Seq.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not ) |> Seq.map (sprintf "#r @\"%s\"")
                yield! project.Files |> Seq.map (sprintf "#load @\"%s\"")
            |]
        promise {
            let path = node.path.join(workspace.rootPath.Value, "references.fsx")
            let! td = vscode.Uri.parse ("untitled:" + path) |> workspace.openTextDocument
            let! te = window.showTextDocument(td, ViewColumn.Three)
            let! _ = te.edit (fun e ->
                let p = vscode.Position.Create(0.,0.)
                let ctn = ctn |> String.concat "\n"
                e.insert(p,ctn))
            return () }

    let activate (context : ExtensionContext) =
        Watcher.activate(!!context.subscriptions)
        SdkScriptsNotify.activate context

        window.registerTerminalProfileProvider("ionide-fsharp.fsi", provider) |> context.Subscribe

        window.onDidCloseTerminal.Invoke(handleCloseTerminal) |> context.Subscribe
        window.onDidOpenTerminal.Invoke(handleOpenTerminal) |> context.Subscribe
        commands.registerCommand("fsi.Start", start |> objfy2) |> context.Subscribe
        commands.registerCommand("fsi.SendLine", sendLine |> objfy2) |> context.Subscribe
        commands.registerCommand("fsi.SendSelection", sendSelection |> objfy2) |> context.Subscribe
        commands.registerCommand("fsi.SendLastSelection", sendLastSelection |> objfy2) |> context.Subscribe
        commands.registerCommand("fsi.SendFile", sendFile |> objfy2) |> context.Subscribe
        commands.registerCommand("fsi.SendText", sendText |> objfy2) |> context.Subscribe
        commands.registerCommand("fsi.SendProjectReferences", sendReferences |> objfy2) |> context.Subscribe
        commands.registerCommand("fsi.GenerateProjectReferences", generateProjectReferences |> objfy2) |> context.Subscribe
        commands.registerCommand("fsi.OpenWatcher", Watcher.openPanel |> objfy2) |> context.Subscribe

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

    type FsiTerminal = {
        Terminal: Terminal
        CWD: string
        File: string
        PID: int option
        LastSelection: string option
    }
    let mutable terminals: Map<string, FsiTerminal> = Map.empty
    let private terminalsLock = obj()

    let updateTerminal (t: FsiTerminal) =
        lock terminalsLock (fun _ ->
            terminals <- Map.add t.File t terminals
        )

    let removeTerminal (t: FsiTerminal) =
        lock terminalsLock (fun _ ->
            terminals <- Map.remove t.File terminals
        )

    let findByPid (pid: int) =
        terminals |> Map.tryPick (fun k t -> if t.PID = Some pid then Some t else None)

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

    let isSdk () =
        Configuration.get false "FSharp.useSdkScripts"

    let tempFsxFile (dir: string) = node.path.join(dir, "tmp.fsx")

    let currentFsiContext () =
        match window.activeTextEditor with
        | Some textEditor ->
            let file = textEditor.document.fileName
            let dir = node.path.dirname file
            file, dir
        | None ->
            let dir = workspace.rootPath.Value
            tempFsxFile dir, dir

    type SendContext = { cwd: string; message: string }
    type CDResult = NoChange of FsiTerminal | Change of FsiTerminal

    let sendCd (fsiTerminal: FsiTerminal) (file, dir) =
        let cdChanged, fsiTerminal' =
            if fsiTerminal.CWD = dir then false, fsiTerminal
            else
                fsiTerminal.Terminal.sendText($"# silentCd @\"%s{dir}\";;\n", false)
                true, { fsiTerminal with CWD = dir }
        let fileChanged, fsiTerminal'' =
            if fsiTerminal'.File = file then false, fsiTerminal'
            else
                fsiTerminal'.Terminal.sendText($"# %d{1} @\"%s{file}\"\n;;\n", false)
                true, { fsiTerminal' with File = file }
        if cdChanged || fileChanged then Change fsiTerminal'' else NoChange fsiTerminal''

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
                        let file, cwd = currentFsiContext()
                        let name =
                            if isSdk ()
                            then fsiNetCoreName
                            else fsiNetFrameworkName

                        let fullName = $"{name}: {file}"

                        let options: TerminalOptions = createEmpty<_>
                        options.name <- Some fullName
                        options.shellArgs <- Some fsiArguments
                        options.shellPath <- Some fsiBinary
                        options.cwd <- Some (U2.Case1 cwd)
                        let profile : TerminalProfile = createEmpty<_>
                        profile.options <- U2.Case1 options
                        return Some profile
                    }
                    |> Promise.toThenable
                Some (U2.Case2 work)
            }

    let tryFindExistingTerminalForWindow () =
        let file, dir = currentFsiContext ()
        let terminal =
            terminals
            |> Map.tryFind file
            |> Option.orElseWith (fun _ -> Map.tryFind (tempFsxFile dir) terminals)
        terminal

    let private start () =
        promise {
            let currentTerminal = tryFindExistingTerminalForWindow ()
            currentTerminal
            |> Option.iter (fun t ->
                t.Terminal.dispose()
                removeTerminal t
            )

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
            let opts =
                match profile.options with
                | U2.Case1 opts -> opts
                | U2.Case2 _ -> failwith "we never provide these opts"
            let terminal = window.createTerminal opts
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

    let private send (msg: string) =
        let terminal = tryFindExistingTerminalForWindow ()
        let msgWithNewline = msg + (if msg.Contains "//" then "\n" else "") + ";;\n"
        match terminal with
        | None -> start () |> Promise.map (fun _ -> tryFindExistingTerminalForWindow().Value)
        | Some fo -> Promise.lift fo
        |> Promise.onSuccess (fun fsiTerm ->
            let file, dir = currentFsiContext ()
            let fsiTerm =
                // don't care about updating map right here because we're about to force an
                // update anyway with the selection send

                match sendCd fsiTerm (file, dir) with
                | NoChange fsiTerm -> fsiTerm
                | Change fsiTerm -> fsiTerm
            fsiTerm.Terminal.show true
            fsiTerm.Terminal.sendText(msgWithNewline, false)
            let fsiTerm = { fsiTerm with LastSelection = Some msgWithNewline }
            updateTerminal fsiTerm
        )
        |> Promise.onFail (fun _ ->
            window.showErrorMessage("Failed to send text to FSI", null) |> ignore
        )

    let private sendLine () =
        let editor = window.activeTextEditor.Value
        let _ = editor.document.fileName
        let pos = editor.selection.start
        let line = editor.document.lineAt pos
        send line.text
        |> Promise.onSuccess (fun _ -> commands.executeCommand("cursorDown", null) |> ignore)
        |> Promise.suppress // prevent unhandled promise exception
        |> ignore

    let private sendSelection () =
        let editor = window.activeTextEditor.Value
        if editor.selection.isEmpty then
            sendLine ()
        else
            let range = vscode.Range.Create(editor.selection.anchor.line, editor.selection.anchor.character, editor.selection.active.line, editor.selection.active.character)
            let text = editor.document.getText range
            send text
            |> Promise.suppress // prevent unhandled promise exception
            |> ignore

    let private sendLastSelection () =
        match tryFindExistingTerminalForWindow () with
        | Some ({ LastSelection = Some message }) ->
            let saveIfNecessary =
                if "FSharp.saveOnSendLastSelection" |> Configuration.get false then
                    window.activeTextEditor.Value.document.save () |> Promise.ofThenable
                else
                    Promise.lift true
            saveIfNecessary
            |> Promise.bind(fun _ -> send message)
            |> Promise.suppress // prevent unhandled promise exception
            |> ignore
        | Some _
        | None -> ()

    let private sendFile () =
        let editor = window.activeTextEditor.Value
        let text = editor.document.getText ()

        send text
        |> Promise.suppress // prevent unhandled promise exception
        |> ignore

    let private sendText (lines : string list) =
        send (String.concat ";;\n" lines)
        |> Promise.suppress // prevent unhandled promise exception
        |> ignore

    let private referenceAssembly (path : ResolvedReferencePath) = $"#r @\"%s{path}\"\n"

    let sendReferencesForProject project =
        project.References
        |> Seq.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not )
        |> Seq.toList
        |> List.map referenceAssembly
        |> String.concat ""
        |> send
        |> Promise.suppress
        |> ignore

    let private sendReferences () =
        match window.activeTextEditor with
        | None -> ()
        | Some editor ->
            editor.document.fileName
            |> Project.tryFindLoadedProjectByFile
            |> Option.iter sendReferencesForProject


    let private handleCloseTerminal (terminal : Terminal) =
        promise {
            match! terminal.processId with
            | Some pid ->
                match findByPid (int pid) with
                | Some terminal ->
                    removeTerminal terminal
                | None -> ()
            | None -> ()
        }
        |> box
        |> Some

    let setPIDForTerminal (pid: Thenable<option<float>>, terminalPath: string) = promise {
        match! pid with
        | Some pid ->
            let pid = int pid
            let terminal = terminals |> Map.tryFind terminalPath
            match terminal with
            | Some terminal ->
                let terminal' = { terminal with PID = Some pid }
                updateTerminal terminal'
            | None -> ()
        | None -> ()
    }

    // when a new terminal is created, if it's FSI and if we don't already have a terminal then setup the state for tracking FSI
    let private handleOpenTerminal (terminal: Terminal) =
        if terminal.name.StartsWith fsiNetCoreName || terminal.name.StartsWith fsiNetFrameworkName
        then
            // clean out any existing terminals for this scope
            match tryFindExistingTerminalForWindow () with
            | Some t ->
                // because this is the only method that adds to the terminals list, we should close an existing terminal for this context
                t.Terminal.dispose()
                removeTerminal t
            | None -> ()

            // new terminal created, must do book-keeping to discover filename and cwd from creation options
            let file = terminal.name.Split(':').[1].Trim()
            let fsiTerminal = {
                Terminal = terminal
                File = file
                CWD = unbox ((unbox terminal.creationOptions) : TerminalOptions).cwd
                PID = None
                LastSelection = None
            }
            updateTerminal fsiTerminal
            setPIDForTerminal (terminal.processId, file) |> ignore
            match sendCd fsiTerminal (currentFsiContext()) with
            | Change t ->
                updateTerminal t
            | NoChange _ -> ()
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

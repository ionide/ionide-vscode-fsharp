namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode

open DTO

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
            Configuration.setGlobal suggestKey (Some(box false))

        let disablePromptForProject () =
            Configuration.set suggestKey (Some(box false))

        let setUseSdk () =
            Configuration.setGlobal useKey (Some(box true))


        let checkForPatternsAndPromptUser () =
            promise {
                if shouldNotifyAboutSdkScripts () then
                    let! choice =
                        window.showInformationMessage (
                            "You are running .Net Core version of FsAutoComplete, we recommend also using .Net Core version of F# REPL (`dotnet fsi`). Should we change your settings (`FSharp.useSdkScripts`). This requires .Net Core 3.X?",
                            [| Message.choice "Update settings"
                               Message.choice "Ignore"
                               Message.choice "Don't show again" |]
                        )

                    match choice with
                    | Some (HasTitle "Update settings") -> do! setUseSdk ()
                    | Some (HasTitle "Ignore") -> do! disablePromptForProject ()
                    | Some (HasTitle "Don't show again") -> do! disablePromptGlobally ()
                    | _ -> ()
            }

        let activate (_context: ExtensionContext) =
            if Configuration.get true suggestKey then
                checkForPatternsAndPromptUser () |> ignore
            else
                ()

    module Watcher =
        open Webviews
        let mutable panel: WebviewPanel option = None

        let updateContent
            (context: ExtensionContext)
            (varsContent: (string * string) option)
            (typesContent: (string * string) option)
            (funcsContent: (string * string) option)
            =
            let (vars, varsScript) = defaultArg varsContent ("", "")
            let (types, typesScript) = defaultArg typesContent ("", "")
            let (funcs, funcsScript) = defaultArg funcsContent ("", "")

            match panel with
            | Some panel ->
                FsWebview.render (
                    context,
                    panel,
                    $"{vars}{types}{funcs}",
                    scripts = [ varsScript; typesScript; funcsScript ]
                )
            | None -> ()

        let openPanel () =
            promise {
                let addWatcher = "FSharp.addFsiWatcher" |> Configuration.get false

                if not addWatcher then
                    let! res =
                        window.showInformationMessage (
                            "FSI Watcher is an experimental feature, and it needs to be enabled with `FSharp.addFsiWatcher` setting",
                            [| Message.choice "Enable"; Message.choice "Ignore" |]
                        )

                    match res with
                    | Some (HasTitle "Enable") -> do! Configuration.setGlobal "FSharp.addFsiWatcher" (Some(box true))
                    | _ -> ()
                else
                    match panel with
                    | Some p -> p.reveal (!! -2, true)
                    | None ->
                        let viewOpts = createObj [ "preserveFocus" ==> true; "viewColumn" ==> -2 ]

                        let p =
                            FsWebview.create (
                                "fsiWatcher",
                                "FSI Watcher",
                                !!viewOpts,
                                enableScripts = true,
                                enableFindWidget = true,
                                enableCommandUris = true,
                                retainContextWhenHidden = true
                            )

                        let onClose () = panel <- None

                        p.onDidDispose.Invoke(!!onClose) |> ignore
                        panel <- Some p
            }

        let varsUri =
            node.path.join (VSCodeExtension.ionidePluginPath (), "watcher", "vars.txt")

        let typesUri =
            node.path.join (VSCodeExtension.ionidePluginPath (), "watcher", "types.txt")

        let funcUri =
            node.path.join (VSCodeExtension.ionidePluginPath (), "watcher", "funcs.txt")


        let handler (context: ExtensionContext) =
            let mutable varsContent = None
            let mutable typesContent = None
            let mutable funcsContent = None

            node.fs.readFile (
                varsUri,
                (fun _ buf ->
                    if not (Utils.isUndefined buf) then
                        let cnt = buf.ToString()

                        if String.IsNullOrWhiteSpace cnt then
                            varsContent <- None
                        else

                            let datagridContent =
                                cnt
                                |> String.split [| '\n' |]
                                |> Array.map (fun row ->
                                    let x = row.Split([| "###IONIDESEP###" |], StringSplitOptions.None)

                                    box
                                        {| name = x[0]
                                           value = x[1]
                                           Type = x[2]
                                           step = x[3] |})
                            // ensure column order
                            let headers = [| "Name", "name"; "Value", "value"; "Type", "Type"; "Step", "step" |]

                            let grid, script = VsHtml.datagrid ("vars-content", datagridContent, headers)

                            varsContent <- Some(html $"<h3>Declared values</h3>{grid}", script)

                            updateContent context varsContent typesContent funcsContent)
            )

            node.fs.readFile (
                funcUri,
                (fun _ buf ->
                    if not (Utils.isUndefined buf) then
                        let cnt = buf.ToString()

                        if String.IsNullOrWhiteSpace cnt then
                            funcsContent <- None
                        else
                            let datagridContent =
                                cnt
                                |> String.split [| '\n' |]
                                |> Array.map (fun row ->
                                    let x = row.Split([| "###IONIDESEP###" |], StringSplitOptions.None)

                                    box
                                        {| name = x[0]
                                           parameters = x[1]
                                           returnType = x[2]
                                           step = x[3] |})

                            let grid, script =
                                VsHtml.datagrid (
                                    "funcs-content",
                                    datagridContent,
                                    [| "Name", "name"; "Parameters", "parameters"; "Return Type", "returnType" |]
                                )

                            funcsContent <- Some(html $"<h3>Declared functions</h3>{grid}", script)

                            updateContent context varsContent typesContent funcsContent)
            )

            node.fs.readFile (
                typesUri,
                (fun _ buf ->
                    if not (Utils.isUndefined buf) then
                        let cnt = buf.ToString()

                        if String.IsNullOrWhiteSpace cnt then
                            typesContent <- None
                        else
                            let extractSignature (str: string) =
                                if str.Contains "#|#" then
                                    "| " + str.Replace("#|#", "</br>| ")
                                else
                                    str

                            let datagridContent =
                                cnt
                                |> String.split [| '\n' |]
                                |> Array.map (fun row ->
                                    let x = row.Split([| "###IONIDESEP###" |], StringSplitOptions.None)

                                    let signature = extractSignature x[1]

                                    box
                                        {| Name = x[0]
                                           Signature = signature
                                           Step = x[2] |})

                            let grid, script = VsHtml.datagrid ("types-content", datagridContent)

                            typesContent <- Some(html $"<h3>Declared types</h3>{grid}", script)

                            updateContent context varsContent typesContent funcsContent)
            )

        let activate context dispsables =
            node.fs.watchFile (varsUri, (fun st st2 -> handler context))
            node.fs.watchFile (typesUri, (fun st st2 -> handler context))
            node.fs.watchFile (funcUri, (fun st st2 -> handler context))

    let mutable fsiOutput: Terminal option = None
    let mutable fsiOutputPID: int option = None
    let mutable lastSelectionSent: string option = None

    let mutable lastCd: string option = None
    let mutable lastCurrentFile: string option = None

    let isSdk () =
        Configuration.get false "FSharp.useSdkScripts"

    let sendCd (textEditor: TextEditor option) =
        let file, dir =
            match textEditor with
            | Some textEditor ->
                let file = textEditor.document.fileName
                let dir = node.path.dirname file
                file, dir
            | None ->
                let dir = workspace.rootPath.Value
                node.path.join (dir, "tmp.fsx"), dir

        match lastCd with
        | Some (cd) when cd = dir -> ()
        | _ ->
            let msg = sprintf "# silentCd @\"%s\";;\n" dir

            fsiOutput |> Option.iter (fun n -> n.sendText (msg, false))

            lastCd <- Some dir

        match lastCurrentFile with
        | Some (currentFile) when currentFile = file -> ()
        | _ ->
            let msg = sprintf "# %d @\"%s\"\n;;\n" 1 file

            fsiOutput |> Option.iter (fun n -> n.sendText (msg, false))

            lastCurrentFile <- Some file

    let fsiBinaryAndParameters () =
        let addWatcher = "FSharp.addFsiWatcher" |> Configuration.get false

        let parms =
            let fsiParams =
                "FSharp.fsiExtraParameters"
                |> Configuration.get Array.empty<string>
                |> List.ofArray

            let p =
                node.path.join (VSCodeExtension.ionidePluginPath (), "watcher", "watcher.fsx")

            let fsiParams =
                if addWatcher then
                    [ "--load:" + p ] @ fsiParams
                else
                    fsiParams

            if Environment.isWin then
                // these flags are added to work around issues with the vscode terminal shell on windows
                [ "--fsi-server-input-codepage:28591"; "--fsi-server-output-codepage:65001" ]
                @ fsiParams
            else
                fsiParams
            |> Array.ofList

        promise {
            if isSdk () then
                let! dotnet = LanguageService.dotnet ()

                match dotnet with
                | Some dotnet ->
                    let! fsiSetting = LanguageService.fsiSdk ()
                    let fsiArg = defaultArg fsiSetting "fsi"
                    return dotnet, [| yield fsiArg; yield! parms |]
                | None -> return failwith "dotnet fsi requested but no dotnet SDK was found."
            else
                let! fsi = LanguageService.fsi ()

                match fsi with
                | Some fsi -> return fsi, parms
                | None -> return failwith ".Net Framework FSI was requested but not found"
        }

    let fsiNetCoreName = "F# Interactive (.Net Core)"
    let fsiNetFrameworkName = "F# Interactive"

    let provider: TerminalProfileProvider =
        { new TerminalProfileProvider with
            override this.provideTerminalProfile(token: CancellationToken) : ProviderResult<TerminalProfile> =
                let work =
                    promise {
                        let! (fsiBinary, fsiArguments) = fsiBinaryAndParameters ()
                        let fsiArguments = U2.Case1(ResizeArray fsiArguments)

                        let name = if isSdk () then fsiNetCoreName else fsiNetFrameworkName

                        let options: TerminalOptions = createEmpty<_>
                        options.name <- Some name
                        options.shellArgs <- Some fsiArguments
                        options.shellPath <- Some fsiBinary
                        let profile: TerminalProfile = createEmpty<_>
                        profile.options <- U2.Case1 options
                        return Some profile
                    }
                    |> Promise.toThenable

                Some(U2.Case2 work) }

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
        |> Seq.tryFind (fun t -> t.name = fsiNetFrameworkName || t.name = fsiNetCoreName)

    let private start () =
        promise {
            let ctok = vscode.CancellationTokenSource.Create().token

            let! profile =
                match provider.provideTerminalProfile (ctok) with
                | None -> promise.Return None
                | Some (U2.Case1 options) -> promise.Return(Some options)
                | Some (U2.Case2 work) -> Promise.ofThenable work

            let profile =
                match profile with
                | Some opts -> opts
                | None ->
                    window.showErrorMessage ("Unable to spawn FSI") |> ignore

                    failwith "unable to spawn FSI"

            let terminal =
                match profile.options with
                | U2.Case1 opts -> window.createTerminal opts
                | U2.Case2 opts -> window.createTerminal opts

            terminal.show (true)
            return terminal
        }
        |> Promise.onFail (fun _ ->
            window.showErrorMessage ("Failed to spawn FSI, please ensure it's in PATH")
            |> ignore)

    let private chunkStringBySize (size: int) (str: string) =
        let mutable i1 = 0

        [ while i1 < str.Length do
              let i2 = min (i1 + size) (str.Length)
              yield str.[i1 .. i2 - 1]
              i1 <- i2 ]

    let private send (msg: string) =
        let msgWithNewline = msg + (if msg.Contains "//" then "\n" else "") + ";;\n"

        match fsiOutput with
        | None -> start ()
        | Some fo -> Promise.lift fo
        |> Promise.onSuccess (fun fp ->
            fp.show true
            fp.sendText (msgWithNewline, false)
            lastSelectionSent <- Some msg)
        |> Promise.onFail (fun _ -> window.showErrorMessage ("Failed to send text to FSI") |> ignore)

    let private moveCursorDownOneLine () =
        let args = createObj [ "to" ==> "down"; "by" ==> "line"; "value" ==> 1 ]

        commands.executeCommand ("cursorMove", Some(box args)) |> ignore

    let private sendLine () =
        let editor = window.activeTextEditor.Value
        let _ = editor.document.fileName
        let pos = editor.selection.start
        let line = editor.document.lineAt pos
        sendCd (Some editor)

        send line.text
        |> Promise.onSuccess (fun _ -> moveCursorDownOneLine ())
        |> Promise.suppress // prevent unhandled promise exception
        |> ignore

    let private sendSelection () =
        let editor = window.activeTextEditor.Value
        let _ = editor.document.fileName

        sendCd (Some editor)

        if editor.selection.isEmpty then
            sendLine ()
        else
            let range =
                vscode.Range.Create(
                    editor.selection.anchor.line,
                    editor.selection.anchor.character,
                    editor.selection.active.line,
                    editor.selection.active.character
                )

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
            |> Promise.bind (fun _ -> send x)
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

    let private sendText (lines: string list) =
        send (String.concat ";;\n" lines)
        |> Promise.suppress // prevent unhandled promise exception
        |> ignore

    let private referenceAssembly (path: ResolvedReferencePath) = path |> sprintf "#r @\"%s\"" |> send
    let private referenceAssemblies = Promise.executeForAll referenceAssembly

    let private sendReferences () =
        window.activeTextEditor.Value.document.fileName
        |> Project.tryFindLoadedProjectByFile
        |> Option.iter (fun p ->
            sendCd window.activeTextEditor

            p.References
            |> Seq.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not)
            |> Seq.toList
            |> referenceAssemblies
            |> Promise.suppress
            |> ignore)


    let private clearOldTerminalState () =
        fsiOutput |> Option.iter (fun t -> t.dispose ())

    let private handleCloseTerminal (terminal: Terminal) =
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
        if terminal.name = fsiNetCoreName || terminal.name = fsiNetFrameworkName then
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
                [| yield!
                       p.References
                       |> Seq.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not)
                       |> Seq.map (sprintf "#r @\"%s\"")
                   yield! p.Files |> Seq.map (sprintf "#load @\"%s\"") |])

        promise {
            match ctn with
            | Some c ->
                let path = node.path.join (workspace.rootPath.Value, "references.fsx")

                let! td = vscode.Uri.parse ("untitled:" + path) |> workspace.openTextDocument

                let! te = window.showTextDocument (td, ViewColumn.Three)

                let! _ =
                    te.edit (fun e ->
                        let p = vscode.Position.Create(0., 0.)
                        let ctn = c |> String.concat "\n"
                        e.insert (p, ctn))


                return ()
            | None -> return ()
        }

    let sendReferencesForProject project =
        project.References
        |> Seq.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not)
        |> Seq.toList
        |> referenceAssemblies
        |> Promise.suppress
        |> ignore

    let generateProjectReferencesForProject project =
        let ctn =
            [| yield!
                   project.References
                   |> Seq.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not)
                   |> Seq.map (sprintf "#r @\"%s\"")
               yield! project.Files |> Seq.map (sprintf "#load @\"%s\"") |]

        promise {
            let path = node.path.join (workspace.rootPath.Value, "references.fsx")

            let! td = vscode.Uri.parse ("untitled:" + path) |> workspace.openTextDocument

            let! te = window.showTextDocument (td, ViewColumn.Three)

            let! _ =
                te.edit (fun e ->
                    let p = vscode.Position.Create(0., 0.)
                    let ctn = ctn |> String.concat "\n"
                    e.insert (p, ctn))

            return ()
        }

    let activate (context: ExtensionContext) =
        Watcher.activate context (!!context.subscriptions)
        SdkScriptsNotify.activate context

        window.registerTerminalProfileProvider ("ionide-fsharp.fsi", provider)
        |> context.Subscribe

        window.onDidCloseTerminal.Invoke(handleCloseTerminal) |> context.Subscribe

        window.onDidOpenTerminal.Invoke(handleOpenTerminal) |> context.Subscribe

        commands.registerCommand ("fsi.Start", start |> objfy2) |> context.Subscribe

        commands.registerCommand ("fsi.SendLine", sendLine |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsi.SendSelection", sendSelection |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsi.SendLastSelection", sendLastSelection |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsi.SendFile", sendFile |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsi.SendText", sendText |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsi.SendProjectReferences", sendReferences |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsi.GenerateProjectReferences", generateProjectReferences |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsi.OpenWatcher", Watcher.openPanel |> objfy2)
        |> context.Subscribe

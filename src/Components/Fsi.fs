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
                    | Some(HasTitle "Update settings") -> do! setUseSdk ()
                    | Some(HasTitle "Ignore") -> do! disablePromptForProject ()
                    | Some(HasTitle "Don't show again") -> do! disablePromptGlobally ()
                    | _ -> ()
            }

        let activate (_context: ExtensionContext) =
            if Configuration.get true suggestKey then
                checkForPatternsAndPromptUser () |> ignore
            else
                ()

    module Watcher =


        let private logger =
            ConsoleAndOutputChannelLogger(Some "FsiWatcher", Level.DEBUG, None, Some Level.DEBUG)

        open Webviews
        let mutable panel: WebviewPanel option = None

        let valuesUpdated = vscode.EventEmitter.Create<unit>()
        let mutable values: (string * string) list = []

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
                    | Some(HasTitle "Enable") -> do! Configuration.setGlobal "FSharp.addFsiWatcher" (Some(box true))
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
                            values <- []
                        else

                            let datagridContent =
                                cnt
                                |> String.split [| '\n' |]
                                |> Array.map (fun row ->
                                    let x = row.Split([| "###IONIDESEP###" |], StringSplitOptions.None)


                                    {| name = x[0]
                                       value = x[1]
                                       Type = x[2]
                                       step = x[3] |})

                            values <- datagridContent |> Array.map (fun x -> x.name, x.value) |> Array.toList
                            // ensure column order
                            let headers = [| "Name", "name"; "Value", "value"; "Type", "Type"; "Step", "step" |]

                            let grid, script = VsHtml.datagrid ("vars-content", !!datagridContent, headers)

                            varsContent <- Some(html $"<h3>Declared values</h3>{grid}", script)

                            updateContent context varsContent typesContent funcsContent
                    else
                        varsContent <- None
                        values <- []

                    valuesUpdated.fire ())
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

        let provider =
            { new InlayHintsProvider with
                member _.onDidChangeInlayHints
                    with get (): Event<unit> option = (Some valuesUpdated.event)
                    and set (v: Event<unit> option): unit = ()

                member this.provideInlayHints
                    (
                        document: TextDocument,
                        range: Vscode.Range,
                        token: CancellationToken
                    ) : ProviderResult<ResizeArray<InlayHint>> =
                    promise {
                        let! symbols =
                            Vscode.commands.executeCommand<ResizeArray<DocumentSymbol>> (
                                "vscode.executeDocumentSymbolProvider",
                                Some(box document.uri)
                            )
                            |> Promise.ofThenable

                        let rec flatten (symbols: ResizeArray<DocumentSymbol>) =
                            symbols
                            |> Seq.map (fun s ->
                                if s.children.Count > 0 then
                                    flatten s.children
                                else
                                    [ s ] |> Seq.ofList)
                            |> Seq.collect id

                        let symbols = flatten symbols

                        let symbolsWithValues =
                            symbols
                            |> Seq.choose (fun s ->
                                match values |> List.tryFind (fun (name, _) -> name = s.name) with
                                | Some(_, value) -> Some(s, value)
                                | None -> None)

                        let hints =
                            symbolsWithValues
                            |> Seq.map (fun (s, value) ->
                                let line = document.lineAt s.range.``start``.line

                                let hint =
                                    vscode.InlayHint.Create(
                                        line.range.``end``,
                                        !!(" == " + value),
                                        InlayHintKind.Parameter
                                    )

                                hint.paddingLeft <- Some true
                                hint)
                            |> ResizeArray

                        logger.Debug("Hints", hints)
                        return hints
                    }
                    |> unbox

                member this.resolveInlayHint(hint: InlayHint, token: CancellationToken) : ProviderResult<InlayHint> =
                    hint |> unbox }

        let activate context selector (dispsables: ResizeArray<Disposable>) =
            languages.registerInlayHintsProvider (selector, provider) |> dispsables.Add
            node.fs.watchFile (varsUri, (fun st st2 -> handler context))
            node.fs.watchFile (typesUri, (fun st st2 -> handler context))
            node.fs.watchFile (funcUri, (fun st st2 -> handler context))

    let mutable fsiTerminal: Terminal option = None
    let mutable fsiTerminalPID: int option = None
    let mutable lastSelectionSent: string option = None

    let mutable lastCd: string option = None
    let mutable lastCurrentFile: string option = None

    let private sendCd (terminal: Terminal) (textEditor: TextEditor option) =
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
        // Same dir as last time, no need to send it
        | Some(cd) when cd = dir -> ()
        | _ ->
            let msg = sprintf "# silentCd @\"%s\";;\n" dir

            terminal.sendText (msg, false)

            lastCd <- Some dir

        match lastCurrentFile with
        // Same file as last time, no need to send it
        | Some(currentFile) when currentFile = file -> ()
        | _ ->
            let msg = sprintf "# %d @\"%s\"\n;;\n" 1 file

            terminal.sendText (msg, false)

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
            let! dotnet = LanguageService.tryFindDotnet ()

            match dotnet with
            | Ok dotnet ->
                let! fsiSetting = LanguageService.fsiSdk ()
                let fsiArg = defaultArg fsiSetting "fsi"
                return dotnet, [| yield fsiArg; yield! parms |]
            | Error msg -> return failwith msg
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

                        let name = fsiNetCoreName

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

    let private start () =
        promise {
            let ctok = vscode.CancellationTokenSource.Create().token

            let! profile =
                match provider.provideTerminalProfile (ctok) with
                | None -> promise.Return None
                | Some work -> work |> Promise.ofMaybeThenable Some

            let profile =
                match profile with
                | Some opts -> opts
                | None ->
                    window.showErrorMessage ("Unable to spawn FSI") |> ignore

                    failwith "unable to spawn FSI"
            // this coercion could be to either type - TerminalOptions _or_ ExtensionTerminalOptions
            // we don't actually care here so I picked the first Case on the U2 here.
            let terminal = window.createTerminal (!!profile.options : TerminalOptions)

            // Wait for the new terminal to be ready
            let! newTerminal =
                Promise.create (fun resolve reject ->
                    window.onDidOpenTerminal.Invoke(fun (terminal: Terminal) ->
                        if terminal.name = fsiNetCoreName || terminal.name = fsiNetFrameworkName then
                            // It can happens that a new terminal is created before the old one is closed
                            // In that case, we need to close the old one first
                            // This case occures when user invoke "FSI: Start" commands with an existing FSI terminal
                            match fsiTerminal with
                            | Some fsiOutput -> fsiOutput.dispose ()
                            | None -> ()

                            // Return the new terminal
                            resolve terminal

                        None)
                    |> ignore)

            newTerminal.show true

            // Set up the new terminal state
            let! terminalId = Promise.ofThenable newTerminal.processId

            fsiTerminalPID <- Option.map int terminalId

            // Reset global information
            lastCd <- None
            lastCurrentFile <- None
            fsiTerminal <- Some terminal

            // initially have to set up the terminal to be in the correct start directory
            sendCd newTerminal window.activeTextEditor

            return newTerminal
        }
        |> Promise.onFail (fun _ ->
            window.showErrorMessage ("Failed to spawn FSI, please ensure it's in PATH")
            |> ignore)

    let private getTerminal () =
        promise {

            let! terminal =
                match fsiTerminal with
                // No terminal found, spawn a new one
                | None -> start ()
                // A terminal already exists, re-use it
                | Some terminal -> Promise.lift terminal

            terminal.show true

            return terminal
        }

    let private send (terminal: Terminal) (msg: string) =
        let msgWithNewline = msg + (if msg.Contains "//" then "\n" else "") + ";;\n" // TODO: Useful ??

        promise {
            terminal.sendText (msgWithNewline, false)
            lastSelectionSent <- Some msg
        }
        |> Promise.onFail (fun _ -> window.showErrorMessage ("Failed to send text to FSI") |> ignore)
        |> Promise.suppress
        |> Promise.map ignore

    let private moveCursorDownOneLine () =
        let args =
            {| ``to`` = "down"
               by = "line"
               value = 1 |}

        commands.executeCommand ("cursorMove", Some(box args)) |> Promise.ofThenable

    let private sendLine () =
        let editor = window.activeTextEditor.Value

        promise {
            let! terminal = getTerminal ()

            let pos = editor.selection.start
            let line = editor.document.lineAt pos

            sendCd terminal (Some editor)

            do! send terminal line.text
            do! moveCursorDownOneLine ()
        }

    let private sendSelection () =
        let editor = window.activeTextEditor.Value

        promise {
            if editor.selection.isEmpty then
                do! sendLine ()
            else
                // Note: Handle terminal stuff only in this part of the if/else branch
                // because sendLine will already handle it for the other branch

                let! terminal = getTerminal ()

                sendCd terminal (Some editor)

                let range =
                    vscode.Range.Create(
                        editor.selection.anchor.line,
                        editor.selection.anchor.character,
                        editor.selection.active.line,
                        editor.selection.active.character
                    )

                let text = editor.document.getText range

                do! send terminal text
        }

    let private sendLastSelection () =
        promise {
            match lastSelectionSent with
            | Some lastSelectionText ->
                // If configuration is set to save before sending, save the file
                if "FSharp.saveOnSendLastSelection" |> Configuration.get false then
                    let! _ = window.activeTextEditor.Value.document.save () |> Promise.ofThenable
                    ()

                let! terminal = getTerminal ()

                do! send terminal lastSelectionText
            | None -> ()
        }

    let private sendFile () =
        let editor = window.activeTextEditor.Value
        let text = editor.document.getText ()

        promise {
            let! terminal = getTerminal ()

            sendCd terminal (Some editor)
            do! send terminal text
        }

    let private handleCloseTerminal (terminal: Terminal) =
        promise {
            match fsiTerminalPID with
            // There is an FSI terminal active, check if it's the one that was closed
            | Some currentTerminalPID ->
                let! closeTerminalPID = Promise.ofThenable terminal.processId

                if Option.map int closeTerminalPID = Some currentTerminalPID then
                    fsiTerminal <- None
                    fsiTerminalPID <- None
                    lastCd <- None
                    lastCurrentFile <- None

            // No existing terminal, ignore
            | None -> ()
        }
        |> Promise.suppress // prevent unhandled promise exception
        |> Promise.start // Start the promise without waiting for it to complete

        None

    let sendReferencesForProject project =
        let references =
            project.References
            |> Seq.filter (fun n -> n.EndsWith "FSharp.Core.dll" |> not && n.EndsWith "mscorlib.dll" |> not)
            |> Seq.toList

        let sendReference terminal (path: ResolvedReferencePath) = send terminal $"#r @\"%s{path}\""

        promise {
            let! terminal = getTerminal ()

            do! Promise.executeForAll (sendReference terminal) references
        }
        |> Promise.suppress
        |> ignore

    let private sendReferences () =
        let projectOpt =
            window.activeTextEditor.Value.document.fileName
            |> Project.tryFindLoadedProjectByFile

        match projectOpt with
        | Some project -> sendReferencesForProject project
        | None -> window.showErrorMessage ("File not in a project") |> ignore

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

    let private generateProjectReferences () =
        let projectOpt =
            window.activeTextEditor.Value.document.fileName
            |> Project.tryFindLoadedProjectByFile

        promise {
            match projectOpt with
            | Some project -> return! generateProjectReferencesForProject project

            | None -> window.showErrorMessage ("File not in a project") |> ignore
        }

    let activate (context: ExtensionContext) =
        Watcher.activate context LanguageService.selector (!!context.subscriptions)
        SdkScriptsNotify.activate context

        window.registerTerminalProfileProvider ("ionide-fsharp.fsi", provider)
        |> context.Subscribe

        window.onDidCloseTerminal.Invoke(handleCloseTerminal) |> context.Subscribe

        commands.registerCommand ("fsi.Start", start |> objfy2) |> context.Subscribe

        commands.registerCommand ("fsi.SendLine", sendLine |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsi.SendSelection", sendSelection |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsi.SendLastSelection", sendLastSelection |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsi.SendFile", sendFile |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsi.SendProjectReferences", sendReferences |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsi.GenerateProjectReferences", generateProjectReferences |> objfy2)
        |> context.Subscribe

        commands.registerCommand ("fsi.OpenWatcher", Watcher.openPanel |> objfy2)
        |> context.Subscribe

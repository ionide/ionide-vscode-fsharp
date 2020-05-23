namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers
open Ionide.VSCode.Helpers.Process
module node = Fable.Import.Node.Exports

module MSBuild =

    let outputChannel = window.createOutputChannel "msbuild"
    let private logger = ConsoleAndOutputChannelLogger(Some "msbuild", Level.DEBUG, Some outputChannel, Some Level.DEBUG)

    type MSbuildHost =
        | MSBuildExe // .net on win, mono on non win
        | DotnetCli
        | Auto

    type private Host () =
        let mutable currentMsbuildHostType : MSbuildHost option = None
        let _onMSbuildHostTypeDidChange = vscode.EventEmitter<MSbuildHost option>()

        member public this.onMSbuildHostTypeDidChange with get () = _onMSbuildHostTypeDidChange.event

        member public this.value
            with get () = currentMsbuildHostType
             and set host =
                currentMsbuildHostType <- host
                _onMSbuildHostTypeDidChange.fire(currentMsbuildHostType)

    let private host = Host ()

    let pickMSbuildHostType () =
        promise {
            let! envMsbuild = LanguageService.msbuild ()
            let! envDotnet = LanguageService.dotnet()

            let hosts =
                [ yield envMsbuild |> Option.map (fun msbuild -> sprintf ".NET (%s)" msbuild, MSbuildHost.MSBuildExe)
                  yield envDotnet |> Option.map (fun dotnet -> sprintf ".NET Core (%s msbuild)" dotnet, MSbuildHost.DotnetCli) ]
                |> List.choose id
                |> Map.ofList

            let hostsLabels = hosts |> Map.toList |> List.map fst |> ResizeArray
            let opts = createEmpty<QuickPickOptions>
            opts.placeHolder <- Some "The msbuild host to use"
            let! chosen = window.showQuickPick(hostsLabels |> U2.Case1, opts)
            return
                if JS.isDefined chosen
                then
                    logger.Debug("user choose host %s", chosen)
                    host.value <- hosts |> Map.tryFind chosen
                    host.value
                else
                    logger.Debug("user cancelled host pick")
                    None
        }


    let loadMSBuildHostCfg () =
        promise {
            let cfg = vscode.workspace.getConfiguration()
            return
                match cfg.get ("FSharp.msbuildHost", "auto") with
                | ".net" -> Some MSbuildHost.MSBuildExe
                | ".net core" -> Some MSbuildHost.DotnetCli
                | "auto" -> Some MSbuildHost.Auto
                | "ask at first use" -> None
                | _ -> None
        }

    let switchMSbuildHostType () =
        promise {
            logger.Debug "switching msbuild host (msbuild <-> dotnet cli)"
            let! h =
                match host.value with
                | Some MSbuildHost.MSBuildExe ->
                    Some MSbuildHost.DotnetCli |> Promise.lift
                | Some MSbuildHost.DotnetCli ->
                    Some MSbuildHost.MSBuildExe |> Promise.lift
                | Some MSbuildHost.Auto | None ->
                    logger.Debug("not yet choosen, try pick one")
                    pickMSbuildHostType ()

            host.value <- h

            return h
        }

    let getMSbuildHostType () =
        match host.value with
        | Some h -> Some h |> Promise.lift
        | None ->
            promise {
                logger.Debug "No MSBuild host selected yet"
                let! hostFromConfig = loadMSBuildHostCfg ()
                let! ho =
                    match hostFromConfig with
                    | Some h -> Some h |> Promise.lift
                    | None -> pickMSbuildHostType ()

                ho |> Option.iter (fun h -> host.value <- Some h)

                return ho
            }

    let tryGetRightHostType (project : string) =
        match Project.isSDKProjectPath project with
        | true -> MSbuildHost.DotnetCli
        | false -> MSbuildHost.MSBuildExe

    let invokeMSBuild project target hostPreference =
        let autoshow =
            let cfg = vscode.workspace.getConfiguration()
            cfg.get ("FSharp.msbuildAutoshow", false)

        let safeproject = sprintf "\"%s\"" project
        let command = sprintf "%s /t:%s" safeproject target
        let executeWithHost host =
            promise {
                let host' =
                    match host with
                    | MSbuildHost.Auto -> tryGetRightHostType project
                    | h -> h

                let! msbuildPath =
                    match host' with
                    | MSbuildHost.MSBuildExe ->
                        LanguageService.msbuild ()
                        |> Promise.bind (function Some msbuild -> Promise.lift msbuild
                                                | None -> Promise.reject "MSBuild binary not found. Please install it from the [Visual Studio Download Page](https://visualstudio.microsoft.com/thank-you-downloading-visual-studio/?sku=BuildTools&rel=15)")
                    | MSbuildHost.DotnetCli ->
                        LanguageService.dotnet ()
                        |> Promise.bind (function Some msbuild -> Promise.lift msbuild
                                                | None -> Promise.reject "dotnet SDK not found. Please install it from the [Dotnet SDK Download Page](https://www.microsoft.com/net/download)")
                    | MSbuildHost.Auto -> Promise.lift ""

                let cmd =
                    match host' with
                    | MSbuildHost.MSBuildExe -> command
                    | MSbuildHost.DotnetCli -> sprintf "msbuild %s" command
                    | MSbuildHost.Auto -> ""
                logger.Info("invoking msbuild from %s on %s for target %s", msbuildPath, safeproject, target)
                let _ =
                    if autoshow then outputChannel.show()
                return! Process.spawnWithNotification msbuildPath "" cmd outputChannel
                        |> Process.toPromise
            }

        let theMSbuildHostType =
            match hostPreference with
            | Some h -> Promise.lift (Some h)
            | None -> getMSbuildHostType ()

        theMSbuildHostType
        |> Promise.bind (fun t ->
            match t with
            | None -> Promise.empty
            | Some h ->
                let progressOpts = createEmpty<ProgressOptions>
                progressOpts.location <- ProgressLocation.Window
                window.withProgress(progressOpts, (fun p ->
                    let pm = createEmpty<ProgressMessage>
                    pm.message <- "Running MsBuild " + target
                    p.report pm
                    executeWithHost h)))

    /// discovers the project that the active document belongs to and builds that
    let buildCurrentProject target =
        logger.Debug("discovering project")
        match window.activeTextEditor.document with
        | Document.FSharp
        | Document.CSharp
        | Document.VB ->
            let currentProject = Project.getLoaded () |> Seq.where (fun p -> p.Files |> List.exists (String.endWith window.activeTextEditor.document.fileName)) |> Seq.tryHead
            match currentProject with
            | Some p ->
                logger.Debug("found project %s", p.Project)
                invokeMSBuild p.Project target None
            | None ->
                logger.Debug("could not find a project that contained the file %s", window.activeTextEditor.document.fileName)
                Promise.empty
        | Document.Other ->
            logger.Debug("I don't know how to handle a project of type %s", window.activeTextEditor.document.languageId)
            Promise.empty

    /// prompts the user to choose a project
    let pickProject placeHolder =
        logger.Debug "pick project"
        let projects = Project.getAll () |> ResizeArray
        if projects.Count = 0 then
            None |> Promise.lift
        else
            promise {
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some placeHolder
                let! chosen = window.showQuickPick(projects |> U2.Case1, opts)
                logger.Debug("user chose project %s", chosen)
                return if JS.isDefined chosen
                       then Some chosen
                       else None
            }

    /// prompts the user to choose a project (if not specified) and builds that project
    let buildProject target projOpt hostOpt =
        promise {
            logger.Debug "building project"
            let! chosen =
                match projOpt with
                | None -> pickProject "Project to build"
                | Some h -> Some h |> Promise.lift
            return! match chosen with
                    | None -> { Code = Some 0; Signal = None } |> Promise.lift
                    | Some proj -> invokeMSBuild proj target hostOpt
        }

    let tryGetRightHost (project : Project) =
        match host.value with
        | Some h -> h
        | None ->
            match Project.isSDKProject project with
            | true -> MSbuildHost.DotnetCli
            | false -> MSbuildHost.MSBuildExe

    let tryGetRightHost' (project : string) =
        match host.value with
        | Some h -> h
        | None ->
            match Project.isSDKProjectPath project with
            | true -> MSbuildHost.DotnetCli
            | false -> MSbuildHost.MSBuildExe

    let buildProjectPath target (project : Project) =
        let host = tryGetRightHost project
        invokeMSBuild project.Project target (Some host)

    let buildProjectPathFast  (project : Project) =
        promise {
            let! (d : CompileResult) = LanguageService.compile project.Project
            return { Code = Some d.Data.Code; Signal = None }
        }

    let restoreMailBox =
        let progressOpts = createEmpty<ProgressOptions>
        progressOpts.location <- ProgressLocation.Window

        MailboxProcessor.Start(fun inbox->
            let rec messageLoop() = async {
                let! (path,continuation) = inbox.Receive()
                let host = tryGetRightHost' path
                do!
                    window.withProgress(progressOpts, (fun p ->
                        let pm = createEmpty<ProgressMessage>
                        pm.message <- sprintf "Restoring: %s" path
                        p.report pm
                        invokeMSBuild path "Restore" (Some host)
                        |> Promise.bind continuation))
                    |> Async.AwaitPromise
                return! messageLoop()
            }
            messageLoop()
        )

    let restoreProject (projOpt: string option) hostOpt =
        buildProject "Restore" projOpt hostOpt
        |> Promise.onSuccess (fun exit ->
            let failed = exit.Code <> Some 0
            match failed, projOpt with
            | false, Some p -> Project.load true p |> unbox
            | true, Some p -> logger.Error("Restore of %s failed with code %i, signal %s", p, exit.Code, exit.Signal)
            | _ -> ()
        )

    let private restoreProjectAsync (path : string) =
        restoreMailBox.Post(path, fun exit ->
            let failed = exit.Code <> Some 0
            if failed then
                logger.Error("Restore of %s failed with code %i, signal %s", path, exit.Code, exit.Signal)
                () |> Promise.lift
            else
                Project.load true path)

    let restoreProjectPath (project : Project) =
        restoreProjectAsync project.Project

    let restoreProjectWithoutParseData (path : string) =
        restoreProjectAsync path

    let buildSolution target sln = promise {
        match host.value with
        | Some h ->
            let! _ = invokeMSBuild sln target (Some h)
            return ()
        | None ->
            let! host = pickMSbuildHostType ()
            match host with
            | Some h ->
                let! _ = invokeMSBuild sln target (Some h)
                return ()
            | None ->
                let! _ = window.showWarningMessage "Host needs to be chosen for solution build"
                return ()
    }

    let buildCurrentSolution target =
        match Project.getLoadedSolution () with
        | Some (Solution e) ->
            buildSolution target e.Path
            |> ignore
        | Some _ ->
            window.showWarningMessage "Solution not loaded - plugin in directory mode"
            |> ignore
        | None ->
            window.showWarningMessage "Solution not loaded"
            |> ignore


    let activate (context : ExtensionContext) =
        let unlessIgnored (path: string) f =
            if Project.isIgnored path then
                unbox ()
            else
                f path

        let initWorkspace _n = Project.initWorkspace ()
        let loadProject path = Project.load false path

        let solutionWatcher = vscode.workspace.createFileSystemWatcher("**/*.sln")
        solutionWatcher.onDidCreate.Invoke(fun n -> unlessIgnored n.fsPath initWorkspace |> unbox) |> ignore
        solutionWatcher.onDidChange.Invoke(fun n -> unlessIgnored n.fsPath initWorkspace |> unbox) |> ignore

        let projectWatcher = vscode.workspace.createFileSystemWatcher("**/*.fsproj")
        projectWatcher.onDidCreate.Invoke(fun n -> unlessIgnored n.fsPath initWorkspace |> unbox) |> ignore
        projectWatcher.onDidChange.Invoke(fun n -> unlessIgnored n.fsPath loadProject |> unbox) |> ignore

        let assetWatcher = vscode.workspace.createFileSystemWatcher("**/project.assets.json")

        let getFsProjFromAssets (n:Uri)=
            let objDir = node.path.dirname n.fsPath
            let fsprojDir = node.path.join(objDir, "..")
            let files = node.fs.readdirSync (U2.Case1 fsprojDir)
            let fsprojOpt = files |> Seq.tryFind(fun n -> n.EndsWith ".fsproj")
            let fsprojOptNotIgnored =
                match fsprojOpt with
                | Some fsproj ->
                    let p = node.path.join(fsprojDir, fsproj)
                    if Project.isIgnored p then
                        None
                    else
                        Some fsproj
                | None ->
                    None
            fsprojOptNotIgnored, fsprojDir

        assetWatcher.onDidDelete.Invoke(fun n ->
            let (fsprojOpt, fsprojDir) = getFsProjFromAssets n
            match fsprojOpt with
            | Some fsproj ->
                let p = node.path.join(fsprojDir, fsproj)
                let fsacCache = node.path.join(fsprojDir, "obj", "fsac.cache")
                node.fs.unlinkSync(U2.Case1 fsacCache)
                restoreProjectWithoutParseData p
                |> unbox
            | None -> undefined
        ) |> context.subscriptions.Add

        assetWatcher.onDidCreate.Invoke(fun n ->
            let (fsprojOpt, fsprojDir) = getFsProjFromAssets n
            match fsprojOpt with
            | Some fsproj ->
                let p = node.path.join(fsprojDir, fsproj)
                Project.load false p
                |> unbox
            | None -> undefined
        ) |> context.subscriptions.Add

        assetWatcher.onDidChange.Invoke(fun n ->
            let (fsprojOpt, fsprojDir) = getFsProjFromAssets n
            match fsprojOpt with
            | Some fsproj ->
                let p = node.path.join(fsprojDir, fsproj)
                Project.load false p
                |> unbox
            | None -> undefined
        ) |> context.subscriptions.Add

        Project.projectNotRestoredLoaded.Invoke(fun n -> restoreProjectWithoutParseData n |> unbox)
        |> context.subscriptions.Add

        let registerCommand com (action : unit -> _) = vscode.commands.registerCommand(com, unbox<Func<obj, obj>> action) |> context.subscriptions.Add
        let registerCommand2 com (action : obj -> obj -> _) = vscode.commands.registerCommand(com, Func<obj, obj, obj>(fun a b -> action a b |> unbox)) |> context.subscriptions.Add

        /// typed msbuild cmd. Optional project and msbuild host
        let typedMsbuildCmd f projOpt hostOpt =
            let p = if JS.isDefined projOpt then Some (unbox<string>(projOpt)) else None
            let h =
                match (if JS.isDefined hostOpt then unbox<int>(hostOpt) else 0) with
                | 1 -> Some MSbuildHost.MSBuildExe
                | 2 -> Some MSbuildHost.DotnetCli
                | 0 | _ -> None
            f p h

        let envMsbuild =
            LanguageService.msbuild ()
            |> Promise.bind (fun p ->
                match p with
                | Some p ->
                    logger.Info("MSBuild (.NET) found at %s", p)
                    Promise.lift p
                | None -> Promise.reject "MSBuild not found"
            )

        let envDotnet =
            LanguageService.dotnet ()
            |> Promise.bind (fun p ->
                match p with
                | Some p -> logger.Info("Dotnet CLI (.NET Core) found at %s", p)
                            Promise.lift p
                | None -> Promise.reject "dotnet not found"
            )

        host.onMSbuildHostTypeDidChange
        |> Event.invoke (fun host ->
            match host with
            | Some MSbuildHost.MSBuildExe ->
                logger.Info("MSBuild (.NET) activated")
            | Some MSbuildHost.DotnetCli ->
                logger.Info("Dotnet CLI (.NET Core) activated")
            | Some MSbuildHost.Auto ->
                logger.Info("Automatic MSBuild detection")
            | None ->
                logger.Info("Active MSBuild: not choosen yet") )
        |> context.subscriptions.Add

        let reloadCfg _ = promise {
            let! hostCfg = loadMSBuildHostCfg ()
            host.value <- hostCfg
        }

        [envMsbuild; envDotnet]
        |> Promise.all
        |> Promise.bind (fun _ -> reloadCfg ())
        |> ignore

        vscode.workspace.onDidChangeConfiguration
        |> Event.invoke reloadCfg
        |> context.subscriptions.Add

        registerCommand "MSBuild.buildCurrent" (fun _ -> buildCurrentProject "Build")
        registerCommand "MSBuild.rebuildCurrent" (fun _ -> buildCurrentProject "Rebuild")
        registerCommand "MSBuild.cleanCurrent" (fun _ -> buildCurrentProject "Clean")

        registerCommand "MSBuild.buildCurrentSolution" (fun _ -> buildCurrentSolution "Build")
        registerCommand "MSBuild.rebuildCurrentSolution" (fun _ -> buildCurrentSolution "Rebuild")
        registerCommand "MSBuild.cleanCurrentSolution" (fun _ -> buildCurrentSolution "Clean")

        registerCommand2 "MSBuild.buildSelected" (typedMsbuildCmd (buildProject "Build"))
        registerCommand2 "MSBuild.rebuildSelected" (typedMsbuildCmd (buildProject "Rebuild"))
        registerCommand2 "MSBuild.cleanSelected" (typedMsbuildCmd (buildProject "Clean"))
        registerCommand2 "MSBuild.restoreSelected" (typedMsbuildCmd restoreProject)

        registerCommand "MSBuild.switchMSbuildHostType" (fun _ -> switchMSbuildHostType ())

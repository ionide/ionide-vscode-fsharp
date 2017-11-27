namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers

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
            let! envMsbuild = Environment.msbuild
            let! envDotnet = Environment.dotnet

            let hosts =
                [ sprintf ".NET (%s)" envMsbuild, MSbuildHost.MSBuildExe
                  sprintf ".NET Core (%s msbuild)" envDotnet, MSbuildHost.DotnetCli ]
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


    let loadMSBuildHostCfg () = promise {
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
                    | MSbuildHost.MSBuildExe -> Environment.msbuild
                    | MSbuildHost.DotnetCli -> Environment.dotnet
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
        |> Promise.bind (function None -> Promise.empty | Some h -> executeWithHost h)

    /// discovers the project that the active document belongs to and builds that
    let buildCurrentProject target =
        logger.Debug("discovering project")
        match window.activeTextEditor.document with
        | Document.FSharp | Document.CSharp | Document.VB ->
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
                    | None -> Promise.empty
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

    let buildProjectWithoutParseData target (path : string) =
        let host = tryGetRightHost' path
        invokeMSBuild path target (Some host)

    let restoreProject projOpt hostOpt =
        buildProject "Restore" projOpt hostOpt
        |> Promise.onSuccess (fun _ ->
            match projOpt with
            | Some p -> Project.load p |> unbox
            | None -> ()
        )

    let restoreProjectPath (project : Project) =
        let host = tryGetRightHost project
        invokeMSBuild project.Project "Restore" (Some host)
        |> Promise.bind (fun _ -> Project.load project.Project)

    let restoreProjectWithoutParseData (path : string) =
        let host = tryGetRightHost' path
        invokeMSBuild path "Restore" (Some host)
        |> Promise.bind (fun _ -> Project.load path)

    let buildSolution target sln =
        match host.value with
        | Some h -> invokeMSBuild sln target (Some h)
        | None ->
            pickMSbuildHostType ()
            |> Promise.bind (fun host ->
            match host with
            | Some h -> invokeMSBuild sln target (Some h)
            | None ->
                window.showWarningMessage "Host needs to be chosen for solution build"
            )

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

    let activate (context: ExtensionContext) =
        let projectWatcher = vscode.workspace.createFileSystemWatcher("**/*.fsproj")
        projectWatcher.onDidCreate.Invoke(fun n -> (Project.initWorkspace (fun _ -> Promise.empty)) |> unbox) |> ignore
        projectWatcher.onDidChange.Invoke(fun n -> Project.load n.fsPath |> unbox) |> ignore

        let solutionWatcher = vscode.workspace.createFileSystemWatcher("**/*.sln")
        solutionWatcher.onDidCreate.Invoke(fun n -> (Project.initWorkspace (fun _ -> Promise.empty)) |> unbox) |> ignore
        solutionWatcher.onDidChange.Invoke(fun n -> (Project.initWorkspace (fun _ -> Promise.empty)) |> unbox) |> ignore

        let assetWatcher = vscode.workspace.createFileSystemWatcher("**/project.assets.json")

        let getFsProjFromAssets (n:Uri)=
            let objDir = Path.dirname n.fsPath
            let fsprojDir = Path.join(objDir, "..")
            let files = Fs.readdirSync (U2.Case1 fsprojDir)
            files |> Seq.tryFind(fun n -> n.EndsWith ".fsproj"), fsprojDir

        assetWatcher.onDidDelete.Invoke(fun n ->
            let (fsprojOpt, fsprojDir) = getFsProjFromAssets n
            match fsprojOpt with
            | Some fsproj ->
                let p = Path.join(fsprojDir, fsproj)
                let fsacCache = Path.join(fsprojDir, "obj", "fsac.cache")
                Fs.unlinkSync(U2.Case1 fsacCache)
                restoreProjectWithoutParseData p
                |> unbox
            | None -> undefined
        ) |> context.subscriptions.Add

        assetWatcher.onDidCreate.Invoke(fun n ->
            let (fsprojOpt, fsprojDir) = getFsProjFromAssets n
            match fsprojOpt with
            | Some fsproj ->
                let p = Path.join(fsprojDir, fsproj)
                Project.load p
                |> unbox
            | None -> undefined
        ) |> context.subscriptions.Add

        assetWatcher.onDidChange.Invoke(fun n ->
            let (fsprojOpt, fsprojDir) = getFsProjFromAssets n
            match fsprojOpt with
            | Some fsproj ->
                let p = Path.join(fsprojDir, fsproj)
                Project.load p
                |> unbox
            | None -> undefined
        ) |> context.subscriptions.Add

        Project.projectNotRestoredLoaded.event.Invoke(fun n -> restoreProjectWithoutParseData n |> unbox)
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
            Environment.msbuild
            |> Promise.map (fun p ->
                logger.Info("MSBuild (.NET) found at %s", p)
                p)

        let envDotnet =
            Environment.dotnet
            |> Promise.map (fun p ->
                logger.Info("Dotnet cli (.NET Core) found at %s", p)
                p)

        host.onMSbuildHostTypeDidChange
        |> Event.invoke (fun host ->
            match host with
            | Some MSbuildHost.MSBuildExe ->
                logger.Info("MSBuild (.NET) activated")
            | Some MSbuildHost.DotnetCli ->
                logger.Info("Dotnet cli (.NET Core) activated")
            | Some MSbuildHost.Auto ->
                logger.Info("Automatic msbuild detection")
            | None ->
                logger.Info("Active msbuild: not choosen yet") )
        |> context.subscriptions.Add

        let reloadCfg = loadMSBuildHostCfg >> Promise.map (fun h -> host.value <- h)

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

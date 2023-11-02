namespace Ionide.VSCode.FSharp

open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode

open DTO
open Ionide.VSCode.Helpers
open Ionide.VSCode.Helpers.Process

module node = Node.Api

module MSBuild =

    let outputChannel = window.createOutputChannel "Ionide: MSBuild"

    let private logger =
        ConsoleAndOutputChannelLogger(Some "msbuild", Level.DEBUG, Some outputChannel, Some Level.DEBUG)

    let private dotnetBinary () =
        LanguageService.tryFindDotnet ()
        |> Promise.bind (function
            | Ok msbuild -> Promise.lift msbuild
            | Error msg -> Promise.reject (exn msg))

    let invokeMSBuildWithCancel project target (cancellationToken: CancellationToken) =

        if cancellationToken.isCancellationRequested then
            promise { return { Code = None; Signal = Some "SIGINT" } }
        else
            let autoshow =
                let cfg = workspace.getConfiguration ()
                cfg.get ("FSharp.msbuildAutoshow", false)

            let command = [ project; $"/t:%s{target}" ]

            let executeWithHost () =
                promise {
                    let! msbuildPath = dotnetBinary ()
                    let cmd = ResizeArray("msbuild" :: command)
                    logger.Info("invoking msbuild from %s on %s for target %s", msbuildPath, project, target)

                    if autoshow then
                        outputChannel.show (?preserveFocus = None)

                    let childProcess = Process.spawnWithNotification msbuildPath cmd outputChannel

                    cancellationToken.onCancellationRequested.Invoke(fun _ ->
                        if (childProcess.connected) then
                            childProcess.kill ("SIGINT")

                        None)
                    |> ignore

                    return! childProcess |> Process.toPromise
                }

            let progressOpts = createEmpty<ProgressOptions>
            progressOpts.location <- U2.Case1 ProgressLocation.Window

            window.withProgress (
                progressOpts,
                (fun p ctok ->
                    promise {
                        let pm =
                            {| message = Some $"Running MSBuild '{target}' target on '{project}'"
                               increment = None |}

                        p.report pm
                        let! response = executeWithHost ()

                        match response.Code with
                        | Some 0 ->
                            p.report (
                                {| message = Some "MSBuild completed successfully"
                                   increment = None |}
                            )

                            return response
                        | Some code ->
                            p.report (
                                {| message = Some $"MSBuild failed with code %d{code}"
                                   increment = None |}
                            )

                            return response
                        | None ->
                            p.report (
                                {| message = Some "MSBuild failed with an unknown error"
                                   increment = None |}
                            )

                            return response
                    }
                    |> Promise.toThenable)
            )
            |> Promise.ofThenable

    let invokeMSBuild project target =
        let cancellationTokenSource = vscode.CancellationTokenSource.Create()
        invokeMSBuildWithCancel project target cancellationTokenSource.token

    let msbuildTasksFilter: TaskFilter =
        let f = createEmpty<TaskFilter>
        f.``type`` <- Some "msbuild"
        f

    /// discovers the project that the active document belongs to and builds that
    let buildCurrentProject (target: string) =
        promise {
            logger.Debug("discovering project for current file")

            match window.activeTextEditor.Value.document with
            | Document.FSharp
            | Document.CSharp
            | Document.VB ->
                let currentProject =
                    Project.getLoaded ()
                    |> Seq.where (fun p ->
                        p.Files
                        |> Seq.exists (String.endWith window.activeTextEditor.Value.document.fileName))
                    |> Seq.tryHead

                match currentProject with
                | Some p ->
                    logger.Debug("found project %s", p.Project)
                    let projectFile = node.path.basename p.Project
                    let! msbuildTasks = tasks.fetchTasks (msbuildTasksFilter)

                    let expectedGroup =
                        match target with
                        | "Build" -> Some vscode.TaskGroup.Build
                        | "Rebuild" -> Some vscode.TaskGroup.Rebuild
                        | "Clean" -> Some vscode.TaskGroup.Clean
                        | _ -> None

                    if expectedGroup = None then
                        return ()

                    let t =
                        msbuildTasks
                        |> Seq.tryFind (fun t -> t.name = projectFile && t.group = expectedGroup)

                    match t with
                    | None -> logger.Debug("no task found for target %s for project %s", target, projectFile)
                    | Some t ->
                        let! ex = tasks.executeTask (t)
                        return ()
                | None ->
                    logger.Debug(
                        "could not find a project that contained the file %s",
                        window.activeTextEditor.Value.document.fileName
                    )

                    return ()
            | _ ->
                logger.Debug(
                    "I don't know how to handle a project of type %s",
                    window.activeTextEditor.Value.document.languageId
                )

                return ()
        }

    /// prompts the user to choose a project
    let pickProject placeHolder =
        logger.Debug "pick project"
        let projects = Project.getLoaded () |> ResizeArray

        if projects.Count = 0 then
            None |> Promise.lift
        else
            promise {
                let items =
                    projects |> Seq.map (fun p -> node.path.basename p.Project) |> ResizeArray

                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some placeHolder
                let! chosen = window.showQuickPick (U2.Case1 items, opts)
                logger.Debug("user chose project %s", chosen)

                match chosen with
                | None -> return None
                | Some chosen -> return projects |> Seq.tryFind (fun p -> node.path.basename p.Project = chosen)
            }

    /// prompts the user to choose a project (if not specified) and builds that project
    let buildProject target =
        promise {
            logger.Debug "building project"

            let! chosen = pickProject "Select a project to build"
            let! msbuildTasks = tasks.fetchTasks msbuildTasksFilter

            return!
                match chosen with
                | None -> Promise.lift ()
                | Some proj ->
                    promise {
                        let projectFile = node.path.basename proj.Project

                        let expectedGroup =
                            match target with
                            | "Build" -> Some vscode.TaskGroup.Build
                            | "Rebuild" -> Some vscode.TaskGroup.Rebuild
                            | "Clean" -> Some vscode.TaskGroup.Clean
                            | _ -> None

                        if expectedGroup = None then
                            return ()

                        let t =
                            msbuildTasks
                            |> Seq.tryFind (fun t -> t.name = projectFile && t.group = expectedGroup)

                        match t with
                        | None -> logger.Debug("no task found for target %s for project %s", target, projectFile)
                        | Some t ->
                            let! ex = tasks.executeTask (t)
                            return ()
                    }
        }

    let buildProjectPath target (project: Project) = invokeMSBuild project.Project target

    let buildProjectPathFast (project: Project) =
        promise {
            let! (d: CompileResult) = LanguageService.compile project.Project

            return
                { Code = Some d.Data.Code
                  Signal = None }
        }

    let private restoreMailBox =
        let progressOpts = createEmpty<ProgressOptions>
        progressOpts.location <- U2.Case1 ProgressLocation.Window

        MailboxProcessor.Start(fun inbox ->
            let rec messageLoop () =
                async {
                    let! (path, continuation) = inbox.Receive()

                    do!
                        window.withProgress (
                            progressOpts,
                            (fun p ctok ->
                                let pm =
                                    {| message = Some("Restoring " + path)
                                       increment = None |}

                                p.report pm

                                invokeMSBuild path "Restore" |> Promise.bind continuation |> Promise.toThenable)
                        )
                        |> Promise.ofThenable
                        |> Async.AwaitPromise

                    return! messageLoop ()
                }

            messageLoop ())

    let restoreProjectAsync (path: string) =
        restoreMailBox.Post(
            path,
            fun exit ->
                let failed = exit.Code <> Some 0

                if failed then
                    logger.Error("Restore of %s failed with code %i, signal %s", path, exit.Code, exit.Signal)
                    () |> Promise.lift
                else
                    Project.load true path
        )

    let restoreKnownProject (project: Project) = restoreProjectAsync project.Project

    let buildSolution target sln =
        promise {
            let solutionFile = node.path.basename sln
            let! msbuildTasks = tasks.fetchTasks msbuildTasksFilter

            let expectedGroup =
                match target with
                | "Build" -> Some vscode.TaskGroup.Build
                | "Rebuild" -> Some vscode.TaskGroup.Rebuild
                | "Clean" -> Some vscode.TaskGroup.Clean
                | _ -> None

            if expectedGroup = None then
                return ()

            let t =
                msbuildTasks
                |> Seq.tryFind (fun t -> t.name = "solution" && t.group = expectedGroup)

            match t with
            | None -> logger.Debug("no task found for target %s for solution %s", target, solutionFile)
            | Some t ->
                let! ex = tasks.executeTask (t)
                return ()
        }

    let buildCurrentSolution target =
        match Project.getLoadedSolution () with
        | Some(Solution e) -> buildSolution target e.Path |> ignore
        | Some _ ->
            window.showWarningMessage ("Solution not loaded - plugin in directory mode")
            |> ignore
        | None -> window.showWarningMessage ("Solution not loaded") |> ignore

    type MSBuildTask = Task

    // type ShellExecutionStatic =
    //     abstract Create()

    let invokeDotnet dotnetPath subcommand args cwd env : ShellExecution =
        let args = (subcommand :: args) |> Seq.map U2.Case1 |> ResizeArray

        let opts = createEmpty<ShellExecutionOptions>
        opts.cwd <- Some cwd
        let envs = createEmpty<ProcessExecutionOptionsEnv>
        env |> Map.iter (fun k v -> envs?k <- v)
        vscode.ShellExecution.Create(U2.Case1 dotnetPath, args, opts)

    let msbuildTaskDef: TaskDefinition =
        let t = createEmpty<TaskDefinition>
        t?``type`` <- "msbuild"
        t

    let private restoreProjectCmd () =
        promise {
            logger.Debug "building project"

            let! chosen = pickProject "Select a project to restore"

            match chosen with
            | None -> return ()
            | Some project ->
                let projectFile = node.path.basename project.Project
                let projectDir = node.path.dirname project.Project
                let! dotnet = dotnetBinary ()
                let execution = invokeDotnet dotnet "restore" [] projectDir Map.empty

                let detailString = $"Restore the {projectFile} project using `dotnet restore`"

                let restoreTask =
                    vscode.Task.Create(
                        msbuildTaskDef,
                        U2.Case2 TaskScope.Workspace,
                        $"{projectFile}",
                        "Restore",
                        U3.Case2 execution,
                        U2.Case1 "$msCompile",
                        group = Some vscode.TaskGroup.Build,
                        detail = Some detailString
                    )

                let! ex = tasks.executeTask restoreTask
                return ()
        }

    let perProjectTasks =
        [ "build", [], vscode.TaskGroup.Build, "Build"
          "clean", [], vscode.TaskGroup.Clean, "Clean"
          "msbuild", [ "/t:Rebuild" ], vscode.TaskGroup.Rebuild, "Rebuild" ]

    let buildTaskListForProject (p: Project) : JS.Promise<MSBuildTask seq> =
        promise {
            let projectName = node.path.basename p.Project
            let projectDir = node.path.dirname p.Project
            let! dotnet = dotnetBinary ()

            return
                seq {
                    for (command, args, group, verb) in perProjectTasks do
                        let execution = invokeDotnet dotnet command args projectDir Map.empty
                        let cmdline = "dotnet" :: command :: args |> String.concat " "
                        let detailString = $"{verb} the {projectName} project using {cmdline}"

                        let t =
                            vscode.Task.Create(
                                msbuildTaskDef,
                                U2.Case2 TaskScope.Workspace,
                                $"{projectName}",
                                verb,
                                U3.Case2 execution,
                                U2.Case1 "$msCompile",
                                group = Some group,
                                detail = Some detailString
                            )

                        yield t
                }
        }

    let buildTaskListForSolution (s: WorkspacePeekFound) : JS.Promise<MSBuildTask seq> =
        promise {
            match s with
            | WorkspacePeekFound.Directory _ -> return Seq.empty
            | WorkspacePeekFound.Solution({ Path = p }) ->
                let! dotnet = dotnetBinary ()
                let solutionDir = node.path.dirname p
                let solutionName = node.path.basename p

                return
                    seq {
                        for (command, args, group, verb) in perProjectTasks do
                            let execution =
                                invokeDotnet dotnet command (solutionName :: args) solutionDir Map.empty

                            let cmdline = "dotnet" :: command :: args |> String.concat " "
                            let detailString = $"{verb} the {solutionName} solution using {cmdline}"

                            let t =
                                vscode.Task.Create(
                                    msbuildTaskDef,
                                    U2.Case2 TaskScope.Workspace,
                                    "solution",
                                    verb,
                                    U3.Case2 execution,
                                    U2.Case1 "$msCompile",
                                    group = Some group,
                                    detail = Some detailString
                                )

                            yield t
                    }

        }

    let traverse (p: ResizeArray<JS.Promise<'t>>) : JS.Promise<ResizeArray<'t> option> =
        promise {
            let! outputs = Promise.all (Array.ofSeq p)
            return Some(ResizeArray(outputs))
        }

    let msbuildBuildTaskProvider =
        { new TaskProvider<MSBuildTask> with
            override x.provideTasks(token: CancellationToken) =
                logger.Info "providing tasks"

                let projectTasks = Project.getLoaded () |> Seq.map buildTaskListForProject

                let solutionTasks =
                    Project.getLoadedSolution ()
                    |> Option.map buildTaskListForSolution
                    |> Option.toList

                let tasks =
                    projectTasks
                    |> Seq.append solutionTasks
                    |> ResizeArray
                    |> traverse
                    |> Promise.map (function
                        | None -> None
                        | Some tasks -> tasks |> Seq.concat |> ResizeArray |> Some)
                    |> Promise.toThenable

                ProviderResult.Some(U2.Case2 tasks)

            override x.resolveTask(t: MSBuildTask, token: CancellationToken) =
                logger.Info("resolving task %s", t.name)
                ProviderResult.Some(U2.Case1 t) }

    let activate (context: ExtensionContext) =
        let unlessIgnored (path: string) f =
            if Project.isIgnored path then unbox () else f path

        let initWorkspace _n = Project.initWorkspace context

        let solutionWatcher = workspace.createFileSystemWatcher (U2.Case1 "**/*.sln")

        solutionWatcher.onDidCreate.Invoke(fun n -> unlessIgnored n.fsPath initWorkspace |> unbox)
        |> ignore

        solutionWatcher.onDidChange.Invoke(fun n -> unlessIgnored n.fsPath initWorkspace |> unbox)
        |> ignore

        //Restore any project that returns NotRestored status
        Project.projectNotRestoredLoaded.Invoke(fun n -> restoreProjectAsync n |> unbox)
        |> context.Subscribe

        let registerCommand com (action: unit -> _) =
            commands.registerCommand (com, action |> objfy2) |> context.Subscribe

        let registerCommand2 com (action: obj -> obj -> _) =
            commands.registerCommand (com, action |> objfy3) |> context.Subscribe

        /// typed msbuild cmd. Optional project and msbuild host
        let typedMsbuildCmd f projOpt =
            let p =
                if JS.isDefined projOpt then
                    Some(unbox<string> (projOpt))
                else
                    None

            fun _ -> f p

        tasks.registerTaskProvider ("msbuild", msbuildBuildTaskProvider)
        |> context.Subscribe

        registerCommand "MSBuild.buildCurrent" (fun _ -> buildCurrentProject "Build")
        registerCommand "MSBuild.rebuildCurrent" (fun _ -> buildCurrentProject "Rebuild")
        registerCommand "MSBuild.cleanCurrent" (fun _ -> buildCurrentProject "Clean")

        registerCommand "MSBuild.buildCurrentSolution" (fun _ -> buildCurrentSolution "Build")
        registerCommand "MSBuild.rebuildCurrentSolution" (fun _ -> buildCurrentSolution "Rebuild")
        registerCommand "MSBuild.cleanCurrentSolution" (fun _ -> buildCurrentSolution "Clean")

        commands.registerCommand (
            "MSBuild.buildSelected",
            fun _ -> buildProject "Build" |> Promise.map box |> box |> Some
        )
        |> context.Subscribe

        commands.registerCommand (
            "MSBuild.rebuildSelected",
            fun _ -> buildProject "Rebuild" |> Promise.map box |> box |> Some
        )
        |> context.Subscribe

        commands.registerCommand (
            "MSBuild.cleanSelected",
            fun _ -> buildProject "Clean" |> Promise.map box |> box |> Some
        )
        |> context.Subscribe

        commands.registerCommand (
            "MSBuild.restoreSelected",
            fun _ -> restoreProjectCmd () |> Promise.map box |> box |> Some
        )
        |> context.Subscribe

namespace Ionide.VSCode.FSharp

open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open global.Node

open DTO
open Ionide.VSCode.Helpers
open Ionide.VSCode.Helpers.Process
module node = Node.Api

module MSBuild =

    let outputChannel = window.createOutputChannel "msbuild"
    let private logger = ConsoleAndOutputChannelLogger(Some "msbuild", Level.DEBUG, Some outputChannel, Some Level.DEBUG)

    let invokeMSBuild project target =
        let autoshow =
            let cfg = vscode.workspace.getConfiguration()
            cfg.get ("FSharp.msbuildAutoshow", false)

        let safeproject = sprintf "\"%s\"" project
        let command = sprintf "%s /t:%s" safeproject target
        let executeWithHost () =
            promise {
                let! msbuildPath =
                    LanguageService.dotnet ()
                    |> Promise.bind (function Some msbuild -> Promise.lift msbuild
                                            | None -> Promise.reject "dotnet SDK not found. Please install it from the [Dotnet SDK Download Page](https://www.microsoft.com/net/download)")

                let cmd = sprintf "msbuild %s" command
                logger.Info("invoking msbuild from %s on %s for target %s", msbuildPath, safeproject, target)
                if autoshow then outputChannel.show()
                return! Process.spawnWithNotification msbuildPath "" cmd outputChannel
                        |> Process.toPromise
            }

        let progressOpts = createEmpty<ProgressOptions>
        progressOpts.location <- ProgressLocation.Window
        window.withProgress(progressOpts, (fun p ->
            let pm = createEmpty<ProgressMessage>
            pm.message <- "Running MsBuild " + target
            p.report pm
            executeWithHost () ))

    /// discovers the project that the active document belongs to and builds that
    let buildCurrentProject target =
        logger.Debug("discovering project")
        match window.activeTextEditor.document with
        | Document.FSharp
        | Document.CSharp
        | Document.VB ->
            let currentProject = Project.getLoaded () |> Seq.where (fun p -> p.Files |> Seq.exists (String.endWith window.activeTextEditor.document.fileName)) |> Seq.tryHead
            match currentProject with
            | Some p ->
                logger.Debug("found project %s", p.Project)
                invokeMSBuild p.Project target
            | None ->
                logger.Debug("could not find a project that contained the file %s", window.activeTextEditor.document.fileName)
                Promise.empty
        | _ ->
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
    let buildProject target projOpt =
        promise {
            logger.Debug "building project"
            let! chosen =
                match projOpt with
                | None -> pickProject "Project to build"
                | Some h -> Some h |> Promise.lift
            return! match chosen with
                    | None -> { Code = Some 0; Signal = None } |> Promise.lift
                    | Some proj -> invokeMSBuild proj target
        }

    let buildProjectPath target (project : Project) =
        invokeMSBuild project.Project target

    let buildProjectPathFast  (project : Project) =
        promise {
            let! (d : CompileResult) = LanguageService.compile project.Project
            return { Code = Some d.Data.Code; Signal = None }
        }

    let private restoreMailBox =
        let progressOpts = createEmpty<ProgressOptions>
        progressOpts.location <- ProgressLocation.Window

        MailboxProcessor.Start(fun inbox->
            let rec messageLoop() = async {
                let! (path,continuation) = inbox.Receive()
                do!
                    window.withProgress(progressOpts, (fun p ->
                        let pm = createEmpty<ProgressMessage>
                        pm.message <- sprintf "Restoring: %s" path
                        p.report pm
                        invokeMSBuild path "Restore"
                        |> Promise.bind continuation))
                    |> Async.AwaitPromise
                return! messageLoop()
            }
            messageLoop()
        )

    let private restoreProjectCmd (projOpt: string option) =
        buildProject "Restore" projOpt
        |> Promise.onSuccess (fun exit ->
            let failed = exit.Code <> Some 0
            match failed, projOpt with
            | false, Some p -> Project.load true p |> unbox
            | true, Some p -> logger.Error("Restore of %s failed with code %i, signal %s", p, exit.Code, exit.Signal)
            | _ -> ()
        )

    let restoreProjectAsync (path : string) =
        restoreMailBox.Post(path, fun exit ->
            let failed = exit.Code <> Some 0
            if failed then
                logger.Error("Restore of %s failed with code %i, signal %s", path, exit.Code, exit.Signal)
                () |> Promise.lift
            else
                Project.load true path)

    let restoreKnownProject (project : Project) =
        restoreProjectAsync project.Project

    let buildSolution target sln = promise {
        let! _ = invokeMSBuild sln target
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

        let solutionWatcher = vscode.workspace.createFileSystemWatcher("**/*.sln")
        solutionWatcher.onDidCreate.Invoke(fun n -> unlessIgnored n.fsPath initWorkspace |> unbox) |> ignore
        solutionWatcher.onDidChange.Invoke(fun n -> unlessIgnored n.fsPath initWorkspace |> unbox) |> ignore

        //Restore any project that returns NotRestored status
        Project.projectNotRestoredLoaded.Invoke(fun n -> restoreProjectAsync n |> unbox)
        |> context.subscriptions.Add

        let registerCommand com (action : unit -> _) = vscode.commands.registerCommand(com, action |> objfy2) |> context.subscriptions.Add
        let registerCommand2 com (action : obj -> obj -> _) = vscode.commands.registerCommand(com, action |> objfy3) |> context.subscriptions.Add

        /// typed msbuild cmd. Optional project and msbuild host
        let typedMsbuildCmd f projOpt =
            let p = if JS.isDefined projOpt then Some (unbox<string>(projOpt)) else None
            fun _ -> f p


        registerCommand "MSBuild.buildCurrent" (fun _ -> buildCurrentProject "Build")
        registerCommand "MSBuild.rebuildCurrent" (fun _ -> buildCurrentProject "Rebuild")
        registerCommand "MSBuild.cleanCurrent" (fun _ -> buildCurrentProject "Clean")

        registerCommand "MSBuild.buildCurrentSolution" (fun _ -> buildCurrentSolution "Build")
        registerCommand "MSBuild.rebuildCurrentSolution" (fun _ -> buildCurrentSolution "Rebuild")
        registerCommand "MSBuild.cleanCurrentSolution" (fun _ -> buildCurrentSolution "Clean")

        registerCommand2 "MSBuild.buildSelected" (typedMsbuildCmd (buildProject "Build"))
        registerCommand2 "MSBuild.rebuildSelected" (typedMsbuildCmd (buildProject "Rebuild"))
        registerCommand2 "MSBuild.cleanSelected" (typedMsbuildCmd (buildProject "Clean"))
        registerCommand2 "MSBuild.restoreSelected" (typedMsbuildCmd restoreProjectCmd)

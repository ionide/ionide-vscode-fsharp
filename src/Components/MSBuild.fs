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
    let private outputChannel = window.createOutputChannel "msbuild"
    let private logger = ConsoleAndOutputChannelLogger(Some "msbuild", Level.DEBUG, Some outputChannel, Some Level.DEBUG)

    type MSbuildHost = 
        | MSBuildExe // .net on win, mono on non win
        | DotnetCli

    let mutable private msbuildHostType : MSbuildHost option = None

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
            let! chosen = window.showQuickPick(hostsLabels |> Case1, opts)
            return
                if JS.isDefined chosen
                then
                    logger.Debug("user choose host %s", chosen)
                    hosts |> Map.tryFind chosen
                else
                    logger.Debug("user cancelled host pick")
                    None
        }

    let switchMSbuildHostType () = 
        logger.Debug "switching msbuild host (msbuild <-> dotnet cli)"
        let next =
            match msbuildHostType with
            | Some MSbuildHost.MSBuildExe ->
                Some MSbuildHost.DotnetCli |> Promise.lift
            | Some MSbuildHost.DotnetCli ->
                Some MSbuildHost.MSBuildExe |> Promise.lift
            | None ->
                logger.Debug("not yet choosen, try pick one")
                pickMSbuildHostType ()
        next
        |> Promise.map (fun h ->
            msbuildHostType <- h
            match msbuildHostType with
            | Some MSbuildHost.MSBuildExe ->
                logger.Debug("using msbuild")
            | Some MSbuildHost.DotnetCli ->
                logger.Debug("using cli")
            | None ->
                logger.Debug("not choosen yet")
            )

    let getMSbuildHostType () =
        match msbuildHostType with
        | Some h -> Some h |> Promise.lift
        | None ->
            logger.Debug "No MSBuild host selected yet"
            let cfg = vscode.workspace.getConfiguration()
            let p =
                match cfg.get ("FSharp.msbuildHost", ".net") with
                | ".net" -> Some MSbuildHost.MSBuildExe |> Promise.lift
                | ".net core" -> Some MSbuildHost.DotnetCli |> Promise.lift
                | "ask at first use" -> pickMSbuildHostType ()
                | _ -> Some MSbuildHost.MSBuildExe |> Promise.lift
            p
            |> Promise.map (fun ho ->
                ho |> Option.iter (fun h -> msbuildHostType <- Some h)
                ho)

    let invokeMSBuildPromise project hostPreference target =
        let autoshow =
            let cfg = vscode.workspace.getConfiguration()
            cfg.get ("FSharp.msbuildAutoshow", true)

        let safeproject = sprintf "\"%s\"" project
        let command = sprintf "%s /t:%s" safeproject target
        let executeWithHost host =
            promise {
                let! msbuildPath =
                    match host with
                    | MSbuildHost.MSBuildExe -> Environment.msbuild
                    | MSbuildHost.DotnetCli -> Environment.dotnet
                let cmd =
                    match host with
                    | MSbuildHost.MSBuildExe -> command
                    | MSbuildHost.DotnetCli -> sprintf "msbuild %s" command
                logger.Debug("invoking msbuild from %s on %s for target %s", msbuildPath, safeproject, target)
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

    let invokeMSBuild project target =
        invokeMSBuildPromise project None target
        |> ignore

    /// discovers the project that the active document belongs to and builds that
    let buildCurrentProject target =
        logger.Debug("discovering project")
        match window.activeTextEditor.document with
        | Document.FSharp | Document.CSharp | Document.VB ->
            let currentProject = Project.getLoaded () |> Seq.where (fun p -> p.Files |> List.exists (String.endWith window.activeTextEditor.document.fileName)) |> Seq.tryHead
            match currentProject with
            | Some p ->
                logger.Debug("found project %s", p.Project)
                invokeMSBuild p.Project target
            | None ->
                logger.Debug("could not find a project that contained the file %s", window.activeTextEditor.document.fileName)
                ()
        | Document.Other -> logger.Debug("I don't know how to handle a project of type %s", window.activeTextEditor.document.languageId)

    /// prompts the user to choose a project and builds that project
    let buildProject target =
        promise {
            logger.Debug "building project"
            let projects = Project.getAll () |> ResizeArray
            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Project to build"
                let! chosen = window.showQuickPick(projects |> Case1, opts)
                logger.Debug("user chose project %s", chosen)
                if JS.isDefined chosen
                then
                    invokeMSBuild chosen target
        }

    let activate disposables =
        let registerCommand com (action : unit -> _) = vscode.commands.registerCommand(com, unbox<Func<obj, obj>> action) |> ignore
        Environment.msbuild
        |> Promise.map (fun p ->
            logger.Debug("MSBuild found at %s", p)
            logger.Debug("MSBuild activated")
        )
        |> ignore
        Environment.dotnet
        |> Promise.map (fun p ->
            logger.Debug("Dotnet cli (.NET Core) found at %s", p)
            logger.Debug("Dotnet cli (.NET Core) activated")
        )
        |> ignore
        registerCommand "MSBuild.buildCurrent" (fun _ -> buildCurrentProject "Build")
        registerCommand "MSBuild.buildSelected" (fun _ -> buildProject "Build")
        registerCommand "MSBuild.rebuildCurrent" (fun _ -> buildCurrentProject "Rebuild")
        registerCommand "MSBuild.rebuildSelected" (fun _ -> buildProject "Rebuild")
        registerCommand "MSBuild.cleanCurrent" (fun _ -> buildCurrentProject "Clean")
        registerCommand "MSBuild.cleanSelected" (fun _ -> buildProject "Clean")
        registerCommand "MSBuild.switchMSbuildHostType" (fun _ -> switchMSbuildHostType ())

        let registerCommand2 com (action : obj -> obj -> _) = vscode.commands.registerCommand(com, Func<obj, obj, obj>(fun a b -> action a b |> unbox)) |> ignore
        registerCommand2 "MSBuild.restore" (fun a b ->
            logger.Debug("a %s", a)
            logger.Debug("b %j", b)
            let host = //TODO define a shared type to use, or make MSbuildHost more high level
                let h = if JS.isDefined b then unbox<int>(b) else 0
                match h with
                | 1 -> Some MSbuildHost.MSBuildExe
                | 2 -> Some MSbuildHost.DotnetCli
                | 0 | _ -> None
            invokeMSBuildPromise (unbox<string>(a)) host "Restore")

namespace Ionide.VSCode.FSharp



open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers
open DTO

[<RequireQualifiedAccess>]
module LaunchJsonVersion2 =

    type [<Pojo>] RequestLaunch =
        { name: string
          ``type``: string
          request: string
          preLaunchTask: string option
          program: string
          args: string array
          cwd: string
          console: string
          stopAtEntry: bool }

    let createRequestLaunch () =
        { RequestLaunch.name = ".NET Core Launch (console)"
          ``type`` = "coreclr"
          request = "launch"
          preLaunchTask = Some "build"
          program = "${workspaceRoot}/bin/Debug/{targetFramework}/{projectName}.dll"
          args = [| |]
          cwd = "${workspaceRoot}"
          console = "externalTerminal"
          stopAtEntry = false }

    type [<Pojo>] RequestAttach =
        { name: string
          ``type``: string
          request: string
          processId: string }

    let createAttachLaunch () =
        { RequestAttach.name = ".NET Core Attach"
          ``type`` = "coreclr"
          request = "attach"
          processId = "${command:pickProcess}" }

    let assertVersion2 (cfg: WorkspaceConfiguration) =
        promise {
            do! cfg.update("version", "0.2.0", false)
            do! cfg.update("configurations", ResizeArray<obj>(), false)
        }

module Debugger =

    let buildAndRun (project : Project) =
        promise {
            let! _ = MSBuild.buildProjectPath "Build" project
            let launcher = Project.getLauncherWithShell project
            match launcher with
            | None ->
                window.showWarningMessage "Can't start project"
                |> ignore
            | Some l ->
                l "" |> ignore
        }

    let setProgramPath project (cfg: LaunchJsonVersion2.RequestLaunch) =
        let relativeOutPath = Path.relative(workspace.rootPath, project.Output).Replace("\\", "/")
        let programPath = sprintf "${workspaceRoot}/%s" relativeOutPath

        // WORKAROUND the project.Output is the obj assembly, instead of bin assembly
        // ref https://github.com/fsharp/FsAutoComplete/issues/218
        let programPath = programPath.Replace("/obj/", "/bin/")

        // WORKAROUND - FSAC reports always net core apps as 1.0
        let programPath = if Project.isNetCoreApp2 project then programPath.Replace("/netcoreapp1.0/", "/netcoreapp2.0/") else programPath

        cfg?program <- programPath

    let debuggerRuntime project =
        match project.Info with
        | ProjectResponseInfo.DotnetSdk dotnetSdk ->
            match dotnetSdk.TargetFrameworkIdentifier with
            | ".NETCoreApp" -> Some "coreclr"
            | _ -> Some "clr"
        | ProjectResponseInfo.Verbose ->
            if Environment.isWin then None else Some "mono"
        | ProjectResponseInfo.ProjectJson -> Some "coreclr"

    let buildAndDebug (project : Project) =
        promise {
            //TODO check if portablepdb, require info from FSAC

            let cfg = LaunchJsonVersion2.createRequestLaunch ()
            match debuggerRuntime project with
            | None ->
                window.showWarningMessage "Can't start debugger"
                |> ignore
            | Some rntm ->
                cfg |> setProgramPath project
                cfg?``type`` <- rntm
                cfg?preLaunchTask <- None

                let folder = workspace.workspaceFolders.[0]
                let! _ = MSBuild.buildProjectPath "Build" project
                // let! res = vscode.commands.executeCommand("vscode.startDebug", cfg)
                let! res =  debug.startDebugging(folder, unbox cfg)
                return ()
        }

    let mutable startup = None
    let mutable context : ExtensionContext option = None

    let setDefaultProject(project : Project) =
        startup <- Some project
        context |> Option.iter (fun c -> c.workspaceState.update("defaultProject", project) |> ignore )

    let chooseDefaultProject () =
        promise {
            let projects =
                Project.getInWorkspace ()
                |> List.choose (fun n ->
                    match n with
                    | Project.ProjectLoadingState.Loaded x -> Some x
                    | _ -> None
                )
            let picks =
                projects
                |> List.map (fun p ->
                    createObj [
                        "data" ==> p
                        "label" ==> p.Project
                    ])
                |> ResizeArray

            let! proj = window.showQuickPick(unbox<U2<ResizeArray<QuickPickItem>, _>> picks)
            let project = unbox<Project> (proj?data)
            setDefaultProject project
            return project
        }


    let buildAndRunDefault () =
        match startup with
        | None ->
            chooseDefaultProject ()
            |> Promise.bind buildAndRun
        | Some p -> buildAndRun p

    let buildAndDebugDefault () =
        match startup with
        | None ->
            chooseDefaultProject ()
            |> Promise.bind buildAndDebug
        | Some p -> buildAndDebug p

    let activate (c : ExtensionContext) =
        commands.registerCommand("fsharp.runDefaultProject", (buildAndRunDefault) |> unbox<Func<obj,obj>> ) |> c.subscriptions.Add
        commands.registerCommand("fsharp.debugDefaultProject", (buildAndDebugDefault) |> unbox<Func<obj,obj>> ) |> c.subscriptions.Add
        commands.registerCommand("fsharp.chooseDefaultProject", (chooseDefaultProject) |> unbox<Func<obj,obj>> ) |> c.subscriptions.Add

        // commands.registerCommand("fsharp.getDefaultProjectPath", Func<obj, obj>(fun m ->
        //     match unbox m with
        //     | Project (path, _, _, _, _, _, pr) ->
        //         MSBuild.buildProjectPath "Build" pr
        //         |> unbox
        //     | _ -> unbox ()
        // )) |> c.subscriptions.Add


        context <- Some c
        startup <- c.workspaceState.get<Project> "defaultProject"
namespace Ionide.VSCode.FSharp

open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open global.Node
open Ionide.VSCode.Helpers
open DTO
module node = Node.Api

[<RequireQualifiedAccess>]
module LaunchJsonVersion2 =

    type RequestLaunch =
        { name : string
          ``type`` : string
          request : string
          preLaunchTask : string option
          program : string
          args : string array
          cwd : string
          console : string
          stopAtEntry : bool }

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

    type RequestAttach =
        { name : string
          ``type`` : string
          request : string
          processId : string }

    let createAttachLaunch () =
        { RequestAttach.name = ".NET Core Attach"
          ``type`` = "coreclr"
          request = "attach"
          processId = "${command:pickProcess}" }

    let assertVersion2 (cfg : WorkspaceConfiguration) =
        promise {
            do! cfg.update("version", Some (box "0.2.0"), U2.Case2 false)
            do! cfg.update("configurations", Some (box (ResizeArray<obj>())), U2.Case2 false)
        }

module Debugger =

    let buildAndRun (project : Project) =
        promise {
            let! _ = MSBuild.buildProjectPath "Build" project
            let launcher = Project.getLauncherWithShell project
            match launcher with
            | None ->
                window.showWarningMessage("Can't start project", null)
                |> ignore
            | Some l ->
                let! terminal = l ""
                terminal.show()
        }

    let setProgramPath project (cfg : LaunchJsonVersion2.RequestLaunch) =
        let relativeOutPath = node.path.relative(workspace.rootPath.Value, project.Output).Replace("\\", "/")
        let programPath = sprintf "${workspaceRoot}/%s" relativeOutPath

        // WORKAROUND the project.Output is the obj assembly, instead of bin assembly
        // ref https://github.com/fsharp/FsAutoComplete/issues/218
        let programPath = programPath.Replace("/obj/", "/bin/")
        cfg?cwd <- node.path.dirname project.Output
        cfg?program <- programPath

    let debuggerRuntime project =
        Some "coreclr"

    let debugProject (project : Project) (args : string []) =
        promise {
            //TODO check if portablepdb, require info from FSAC

            let cfg = LaunchJsonVersion2.createRequestLaunch ()
            match debuggerRuntime project with
            | None ->
                window.showWarningMessage("Can't start debugger", null)
                |> ignore
            | Some rntm ->
                cfg |> setProgramPath project
                cfg?``type`` <- rntm
                cfg?preLaunchTask <- None
                cfg?args <- args

                let debugConfiguration = cfg |> box |> unbox

                let folder = workspace.workspaceFolders.Value.[0]
                let! _ = debug.startDebugging(Some folder, U2.Case2 debugConfiguration)
                return ()
        }

    let buildAndDebug (project : Project) =
        promise {
            //TODO check if portablepdb, require info from FSAC

            let cfg = LaunchJsonVersion2.createRequestLaunch ()
            match debuggerRuntime project with
            | None ->
                window.showWarningMessage("Can't start debugger", null)
                |> ignore
            | Some rntm ->
                cfg |> setProgramPath project
                cfg?``type`` <- rntm
                cfg?preLaunchTask <- None
                let debugConfiguration = cfg |> box |> unbox

                let folder = workspace.workspaceFolders.Value.[0]
                let! msbuildExit = MSBuild.buildProjectPath "Build" project
                match msbuildExit.Code with
                    | Some code when code <> 0 ->
                        return! Promise.reject (sprintf "msbuild 'Build' failed with exit code %i" code)
                    | _ ->
                        let! res =  debug.startDebugging(Some folder, U2.Case2 debugConfiguration)
                        return ()
        }

    let mutable startup = None
    let mutable context : ExtensionContext option = None

    let setDefaultProject(project : Project) =
        startup <- Some project
        context |> Option.iter (fun c -> c.workspaceState.update("defaultProject", Some (box project)) |> ignore )

    let chooseDefaultProject () =
        promise {
            let projects =
                Project.getInWorkspace ()
                |> List.choose (fun n ->
                    match n with
                    | Project.ProjectLoadingState.Loaded x -> Some x
                    | _ -> None
                )

            if projects.Length = 0 then
                return None
            elif projects.Length = 1 then
                return Some projects.Head
            else
                let picks =
                    projects
                    |> List.map (fun p ->
                        createObj [
                            "data" ==> p
                            "label" ==> p.Project
                        ])
                    |> ResizeArray

                let! proj = window.showQuickPick(unbox<U2<ResizeArray<QuickPickItem>, _>> picks)
                if JS.isDefined proj then
                    let project = unbox<Project> (proj?data)
                    setDefaultProject project
                    return Some project
                else
                    return None
        }

    let buildAndRunDefault () =
        match startup with
        | None ->
            chooseDefaultProject ()
            |> Promise.map (Option.map (buildAndRun) >> ignore)
        | Some p -> buildAndRun p

    let buildAndDebugDefault () =
        match startup with
        | None ->
            chooseDefaultProject ()
            |> Promise.map (Option.map (buildAndDebug) >> ignore)
        | Some p -> buildAndDebug p


    let activate (c : ExtensionContext) =
        commands.registerCommand("fsharp.runDefaultProject", (buildAndRunDefault) |> objfy2 ) |> c.Subscribe
        commands.registerCommand("fsharp.debugDefaultProject", (buildAndDebugDefault) |> objfy2 ) |> c.Subscribe
        commands.registerCommand("fsharp.chooseDefaultProject", (chooseDefaultProject) |> objfy2 ) |> c.Subscribe

        context <- Some c
        startup <- c.workspaceState.get<Project> "defaultProject"

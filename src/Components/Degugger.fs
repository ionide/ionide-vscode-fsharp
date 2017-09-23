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
namespace Ionide.VSCode.FSharp



open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers
open DTO

module Debugger =

    type DebuggerConfig = {
        name : string
        ``type`` : string
        request : string
        program: string
        args : string []
        cwd : string
        stopAtEntry: bool
        console : string
        preLaunchTask : string
    }

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

    let buildAndDebug (project : Project) =
        promise {
            let! _ = MSBuild.buildProjectPath "Build" project
            match Project.isSDKProject project with
            | false -> window.showWarningMessage "Can't debug non-SDK project" |> ignore
            | true ->
                match Project.isPortablePdbProject project with
                | false -> window.showWarningMessage "Debugger requires PortablePdb DebugType" |> ignore
                | true ->
                    let typ =
                        match Project.isNetCoreApp project with
                        | true -> "coreclr"
                        | false -> "clr"

                    let p = path.relative(workspace.rootPath, project.Output).Replace("\\", "/")

                    let cfg = {
                        name = "Launch"
                        ``type`` = typ
                        request = "launch"
                        program = "${workspaceRoot}/" + p
                        args = [||]
                        cwd = "${workspaceRoot}"
                        stopAtEntry = true
                        console = "integratedTerminal"
                        preLaunchTask = ""
                    }
                    let folder = workspace.workspaceFolders.[0]
                    printfn "FOLDER: %A" folder
                    printfn "CONFIGH: %A" cfg
                    let! res = vscode.commands.executeCommand("vscode.startDebug", cfg)
                    // let! res =  debug.startDebugging(folder, unbox cfg)
                    printfn "DEBUGER RESULT: %A" res


        }
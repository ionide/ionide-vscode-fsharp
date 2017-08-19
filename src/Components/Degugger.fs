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
                let! cp = l ""
                cp
                |> Process.onOutput (fun out -> printfn "%s" (out.ToString ()) )
                |> Process.toPromise
                ()
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

                    let cfg = {
                        name = "F# Debugging"
                        ``type`` = typ
                        request = "launch"
                        program = project.Output
                        args = [||]
                        cwd = workspace.rootPath
                        stopAtEntry = false
                        console = "externalTerminal"
                    }
                    let folder = workspace.workspaceFolders.[0]
                    debug.startDebugging(folder, unbox cfg)
                    |> ignore


        }
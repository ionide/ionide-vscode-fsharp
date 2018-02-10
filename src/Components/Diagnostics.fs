namespace Ionide.VSCode.FSharp

open System
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open Ionide.VSCode.Helpers

module Diagnostics =

    let toggleDiagnosticsMode () =
        let cfg = workspace.getConfiguration()

        let newChannel =
            match cfg.get("FSharp.logLanguageServiceRequests", "output") with
            | "both" -> "output"
            | _ -> "both"

        let newLevel =
            match cfg.get("FSharp.logLanguageServiceRequestsOutputWindowLevel", "INFO") with
            | "DEBUG" -> "INFO"
            | _ -> "DEBUG"

        promise {
            do! cfg.update("FSharp.logLanguageServiceRequests", newChannel, JS.undefined)
            do! cfg.update("FSharp.logLanguageServiceRequestsOutputWindowLevel", newLevel, JS.undefined)

            if newChannel = "output" then
                return! window.showInformationMessage("Diagnostic disabled, please reload the window.")
            else
                return! window.showInformationMessage("Diagnostic activated, please reload the window.")
        }

    let execCommand path (args: string list) : JS.Promise<string> =
        Promise.create (fun resolve reject ->

            ChildProcess.spawn(path, args |> ResizeArray)
            |> Process.onOutput (fun buffer ->
                let outputString = buffer.toString()
                resolve(outputString)
            )
            |> Process.onError (fun e ->
                let error = unbox<JS.Error> e
                reject (error.message)
            )
            |> Process.onErrorOutput (fun e ->
                let error = unbox<JS.Error> e
                reject (error.message)
            )
            |> ignore
        )
        |> Promise.onFail (fun error ->
            Fable.Import.Browser.console.error(
                """
["IONIDE-DIAGNOSTICS"]
Failed to execute command:
- path: %s
- args: %s
Error: %s
                """.Trim(),
                path,
                args |> String.concat " ",
                error
            )
        )

    module Templates =

        let header =
            """
<!-- Please copy/paste this file content into a Github issue -->
### Problem

<!-- Describe here your problem -->

### Steps to reproduce

<!-- Add here the step to reproduce you problem. Example: -->
<!-- 1. Open an F# file -->
<!-- 2. Ctrl + P > "F# Add Reference" -->
            """.Trim()

        let machineInfos os arch vscode=
            let txt =
                sprintf
                    """
### Machine infos

* Operating system: **%s**
* Arch: **%s**
* VSCode: **%s**
                    """
                    os
                    arch
                    vscode
            txt.Trim()

        let netcoreRuntime dotnetVersion =
            let txt =
                sprintf
                    """
* Runtime: **netcore**
* Dotnet version: **%s**
                    """
                    dotnetVersion
            txt.Trim()

        let monoRuntime monoVersion msbuildVersion =
            let txt =
                sprintf
                    """
* Runtime: **.Net**
* Mono version:
```shell
%s
```
* MSBuild version:
```shell
%s
```
                    """
                    monoVersion
                    msbuildVersion
            txt.Trim()

        let msbuildInfo msbuildVersion =
            let txt =
                sprintf
                    """
* MSBuild version:
```shell
%s
```
                    """
                    msbuildVersion
            txt.Trim()

        let fsacLog =
            """
<!-- You can also linked the FSAC log file into your issue -->
<!-- Use `Ctrl+P > "F#: Get FSAC logs"` commands to get file location -->
            """.Trim()

    let getMSBuildVersion () =
        promise {
            let! msbuild = Environment.msbuild
            let! version = execCommand msbuild [ "/version" ]
            return version.Trim()
        }

    let getMonoVersion () =
        promise {
            let mono = "FSharp.monoPath" |> Configuration.get "mono"
            let! version = execCommand mono [ "--version" ]
            return version.Trim()
        }

    let getRuntimeInfos () =
        promise {
            let fsacRuntime = "FSharp.fsacRuntime" |> Configuration.get "net"

            if fsacRuntime = "netcore" then
                let! dotnet = Environment.dotnet
                let! version = execCommand dotnet [ "--version" ]
                return Templates.netcoreRuntime version
            else
                if Process.isMono () then
                    let! monoVersion = getMonoVersion()
                    let! msbuildVersion = getMSBuildVersion ()
                    return Templates.monoRuntime monoVersion msbuildVersion
                else
                    let! msbuildVersion = getMSBuildVersion ()
                    return Templates.msbuildInfo msbuildVersion
        }

    let writeToFile (text : string) =
        promise {
            let path = Path.join(workspace.rootPath, "Diagnostric info")
            let newFile = Uri.parse ("untitled:" + path)
            let! document = newFile |> workspace.openTextDocument

            let edit = vscode.WorkspaceEdit()
            edit.insert(newFile, vscode.Position(0., 0.), text)
            let! success = vscode.workspace.applyEdit(edit)
            if success then
                vscode.window.showTextDocument(document)
                |> ignore
            else
                vscode.window.showErrorMessage("Error when printing diagnostic report.")
                |> ignore
        }

    let getDiagnosticsInfos () =
        let os = Node.Exports.Os.``type``() |> string
        let arch = Node.Exports.Os.arch() |> string

        promise {
            let! runtimeInfos = getRuntimeInfos ()

            Templates.header + "\n\n"
                + Templates.machineInfos os arch vscode.version + "\n"
                + runtimeInfos + "\n"
                + Templates.fsacLog
            |> writeToFile
            |> ignore
        }

    let getIonideLogs () =
        let writeStream =
            Exports.Path.join(Exports.Os.homedir(), "ionide", "FSAC_logs")
            |> Environment.ensureDirectory
            |> fun dir -> Exports.Path.join(dir, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.log"))
            |> Exports.Fs.createWriteStream

        Promise.create(fun resolve reject ->
            writeStream.on("error", reject) |> ignore
            writeStream.on("close", (fun _ -> resolve writeStream.path)) |> ignore
            writeStream.write(Logging.getIonideLogs ()) |> ignore
            writeStream.close()
        )
        |> Promise.bind(fun path ->
            vscode.window.showInformationMessage(
                "FSAC logs exported to: " + path,
                "Open file"
            )
            |> Promise.bind (fun action ->
                match action with
                | "Open file" ->
                    path
                    |> workspace.openTextDocument
                    |> Promise.bind (fun document ->
                        vscode.window.showTextDocument(document) |> ignore
                        JS.undefined
                    )
                | _ -> JS.undefined
            )
        )
        |> Promise.onFail(fun error ->
            Fable.Import.Browser.console.error(error)
            vscode.window.showErrorMessage("Couldn't retrieved the FSAC logs file") |> ignore
        )

    let activate (context: ExtensionContext) =
        commands.registerCommand("fsharp.diagnostics.toggle", toggleDiagnosticsMode |> unbox<Func<obj,obj>> )
        |> context.subscriptions.Add

        commands.registerCommand("fsharp.diagnostics.getInfos", getDiagnosticsInfos |> unbox<Func<obj,obj>> )
        |> context.subscriptions.Add

        commands.registerCommand("fsharp.diagnostics.getIonideLogs", getIonideLogs |> unbox<Func<obj,obj>>)
        |> context.subscriptions.Add

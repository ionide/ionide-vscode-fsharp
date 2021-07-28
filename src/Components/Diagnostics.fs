namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Import
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open global.Node

open Ionide.VSCode.Helpers
module node = Node.Api

module Diagnostics =

    let execCommand path (args : string list) : JS.Promise<string> =
        Promise.create (fun resolve reject ->

            node.childProcess.spawn(path, args |> ResizeArray)
            |> Process.onOutput (fun buffer ->
                let outputString = buffer.toString()
                resolve(outputString)
            )
            |> Process.onError (fun e ->
                let error = unbox<Exception> e
                reject error
            )
            |> Process.onErrorOutput (fun e ->
                let error = unbox<Exception> e
                reject error
            )
            |> ignore
        )
        |> Promise.onFail (fun error ->
            Browser.Dom.console.error(
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

        let machineInfos os arch vscode =
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

        let netcoreRuntime (dotnetVersion : string) =
            let txt =
                sprintf
                    """
* Runtime: **netcore**
* Dotnet version: **%s**
                    """
                    (dotnetVersion.Trim())
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
            let! msbuild =
                LanguageService.dotnet()
                |> Promise.bind (fun msb -> match msb with Some msb -> Promise.lift msb | None -> Promise.reject "MsBuild not found")

            let! version = execCommand msbuild [ "msbuild /version" ]
            return version.Trim()
        }


    let getRuntimeInfos () =
        let netcoreInfos = promise {
            let! dotnet = Environment.dotnet
            match dotnet with
            | Some dotnet ->
                let! version = execCommand dotnet [ "--version" ]
                return Templates.netcoreRuntime version
            | None -> return "No dotnet installation found"
        }
        let msBuildInfos = promise {
            let! msbuildVersion = getMSBuildVersion ()
            return Templates.msbuildInfo msbuildVersion
        }
        Promise.all [netcoreInfos; msBuildInfos]
        |> Promise.map (String.concat "\n")

    let writeToFile (text : string) =
        promise {
            let path = node.path.join(workspace.rootPath.Value, "Diagnostic info")
            let newFile = vscode.Uri.parse ("untitled:" + path)
            let! document = newFile |> workspace.openTextDocument

            let edit = vscode.WorkspaceEdit.Create()
            edit.insert(newFile, vscode.Position.Create(0., 0.), text)
            let! success = workspace.applyEdit(edit)
            if success then
                window.showTextDocument(document, ?options = None) |> ignore
            else
                window.showErrorMessage("Error when printing diagnostic report.", null)
                |> ignore
        }

    let getDiagnosticsInfos () =
        let os = node.os.``type``() |> string
        let arch = node.os.arch() |> string

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
            node.path.join(Api.os.tmpdir(), "ionide", "FSAC_logs")
            |> Environment.ensureDirectory
            |> fun dir -> Api.path.join(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss.log"))
            |> Api.fs.createWriteStream

        Promise.create(fun resolve reject ->
            writeStream.on("error", reject) |> ignore
            writeStream.on("close", (fun _ -> resolve writeStream.path)) |> ignore
            writeStream.write(Logging.getIonideLogs ()) |> ignore
            writeStream.close()
        )
        |> Promise.bind(fun path -> promise {
            let! action =
                window.showInformationMessage(
                    "FSAC logs exported to: " + path,
                    ResizeArray ["Open file"]
                )
            match action with
            | Some "Open file" ->
                return! promise {
                    let! document = workspace.openTextDocument path
                    window.showTextDocument(document, ?options = None) |> ignore
                    return JS.undefined
                }
            | _ -> return JS.undefined
        }
        )
        |> Promise.onFail(fun error ->
            Browser.Dom.console.error(error)
            window.showErrorMessage("Couldn't retrieved the FSAC logs file", null) |> ignore
        )


    let activate (context : ExtensionContext) =
        commands.registerCommand("fsharp.diagnostics.getInfos", getDiagnosticsInfos |> objfy2)
        |> box
        |> unbox
        |> context.subscriptions.Add

        commands.registerCommand("fsharp.diagnostics.getIonideLogs", getIonideLogs |> objfy2)
        |> box
        |> unbox
        |> context.subscriptions.Add

namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Import
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open global.Node
open JsInterop

open Ionide.VSCode.Helpers

module node = Node.Api

module Diagnostics =

    let execCommand path (args: string list) : JS.Promise<string> =
        Promise.create (fun resolve reject ->

            node.childProcess.spawn (path, args |> ResizeArray)
            |> Process.onOutput (fun buffer ->
                let outputString = buffer.toString ()
                resolve (outputString))
            |> Process.onError (fun e ->
                let error = unbox<Exception> e
                reject error)
            |> Process.onErrorOutput (fun e ->
                let error = unbox<Exception> e
                reject error)
            |> ignore)
        |> Promise.onFail (fun error ->
            Browser.Dom.console.error (
                """
["IONIDE-DIAGNOSTICS"]
Failed to execute command:
- path: %s
- args: %s
Error: %A
                """
                    .Trim(),
                path,
                args |> String.concat " ",
                error.ToString()
            ))

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
            """
                .Trim()

        let machineInfos os arch vscode uiKind extension =
            let txt =
                sprintf
                    """
### Machine infos

* Operating system: **%s**
* Arch: **%s**
* VSCode: **%s**
* UI Kind: **%s**
* Ionide: **%s**

                    """
                    os
                    arch
                    vscode
                    uiKind
                    extension

            txt.Trim()

        let netcoreRuntime (dotnetVersion: string) =
            let txt =
                sprintf
                    """
* Runtime: **netcore**
* Dotnet version: **%s**
                    """
                    (dotnetVersion.Trim())

            txt.Trim()


    let getRuntimeInfos () =
        let netcoreInfos =
            promise {
                let! dotnet = Environment.dotnet

                match dotnet with
                | Some dotnet ->
                    let! version = execCommand dotnet [ "--version" ]
                    return Templates.netcoreRuntime version
                | None -> return "No dotnet installation found"
            }



        Promise.all [ netcoreInfos]
        |> Promise.map (String.concat "\n")

    let writeToFile (text: string) =
        promise {
            let path = node.path.join (workspace.rootPath.Value, "Diagnostic info")
            let newFile = vscode.Uri.parse ("untitled:" + path)
            let! document = newFile |> workspace.openTextDocument

            let edit = vscode.WorkspaceEdit.Create()
            edit.insert (newFile, vscode.Position.Create(0., 0.), text)
            let! success = workspace.applyEdit (edit)

            if success then
                window.showTextDocument (document, ?options = None)
                |> ignore
            else
                window.showErrorMessage ("Error when printing diagnostic report.", null)
                |> ignore
        }

    let getDiagnosticsInfos () =
        let os = node.os.``type`` () |> string
        let arch = node.os.arch () |> string
        let extension =
            extensions.getExtension("ionide.ionide-fsharp")
            |> Option.bind (fun e -> e.packageJSON |> Option.map (fun e-> e?version))
            |> Option.defaultValue "unknown"

        let uiKind =
            match env.uiKind with
            | UIKind.Desktop -> "Desktop"
            | UIKind.Web -> "Web"
            | _ -> "Unknown"

        promise {
            let! runtimeInfos = getRuntimeInfos ()

            Templates.header
            + "\n\n"
            + Templates.machineInfos os arch vscode.version uiKind extension
            + "\n"
            + runtimeInfos
            + "\n"
            |> writeToFile
            |> ignore
        }


    let activate (context: ExtensionContext) =
        commands.registerCommand ("fsharp.diagnostics.getInfos", getDiagnosticsInfos |> objfy2)
        |> context.Subscribe

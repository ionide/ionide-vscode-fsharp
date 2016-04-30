namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.languages
open FunScript.TypeScript.path
open FunScript.TypeScript.fs

open DTO
open Ionide.VSCode.Helpers

[<ReflectedDefinition>]
module Forge =

    let (</>) a b =
        if  Process.isWin ()
        then a + @"\" + b
        else a + "/" + b

    let private location = (VSCode.getPluginPath "Ionide.Ionide-fsharp") </> "bin_forge" </> "Forge.exe"

    let private spawnForge cmd =
        let outputChannel = window.Globals.createOutputChannel "Forge"
        outputChannel.clear ()
        outputChannel.append (location+"\n")

        Process.spawnWithNotification location "mono" cmd outputChannel
        |> ignore

    let onFsFileCreateHandler (uri : Uri) = 
        sprintf "add file -n %s" uri.fsPath |> spawnForge

    let onFsFileRemovedHandler (uri : Uri) = 
        sprintf "remove file -n %s" uri.fsPath |> spawnForge

    let activate disposables = 
        let watcher = workspace.Globals.createFileSystemWatcher ("**/*.fs")
        watcher.onDidCreate.Add(onFsFileCreateHandler, null, disposables)
        watcher.onDidDelete.Add(onFsFileRemovedHandler, null, disposables)
        () 
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

    let private spawnForge (cmd : string) =
        let cmd = cmd.Replace("\r", "").Replace("\n", "")
        let outputChannel = window.Globals.createOutputChannel "Forge"
        outputChannel.clear ()
        outputChannel.append ("forge " + cmd + "\n")

        Process.spawnWithNotification location "mono" cmd outputChannel
        |> ignore

    let private execForge cmd =
        Process.exec location "mono" cmd
        
    let private handleForgeList (error : FunScript.TypeScript.Error, stdout : Buffer, stderr : Buffer) =
        if(stdout.toString() = "") then
            [||]
        else
            stdout.toString().Split('\n')
            |> Array.filter((<>) "" )

    let onFsFileCreateHandler (uri : Uri) = 
        sprintf "add file -n %s" uri.fsPath |> spawnForge

    let onFsFileRemovedHandler (uri : Uri) = 
        sprintf "remove file -n %s" uri.fsPath |> spawnForge
        
    let moveFileUp () = 
        let editor = vscode.window.Globals.activeTextEditor
        if editor.document.languageId = "fsharp" then
            sprintf "move file -n %s -u" editor.document.fileName |> spawnForge
    
    let moveFileDown () =
        let editor = vscode.window.Globals.activeTextEditor
        if editor.document.languageId = "fsharp" then
            sprintf "move file -n %s -d" editor.document.fileName |> spawnForge
            
    let newProject () = 
        "list templates"
        |> execForge
        |> Promise.success handleForgeList
        |> window.Globals.showQuickPick
        |> Promise.toPromise
        |> Promise.success (fun template ->
            if JS.isDefined template then
                let opts = createEmpty<InputBoxOptions> ()
                opts.prompt <- "Project directory" 
                window.Globals.showInputBox (opts)
                |> Promise.toPromise
                |> Promise.success (fun dir ->
                    let opts = createEmpty<InputBoxOptions> ()
                    opts.prompt <- "Project name"
                    window.Globals.showInputBox(opts)
                    |> Promise.toPromise
                    |> Promise.success (fun name ->
                        sprintf "new project -n %s -t %s --folder %s" name template dir
                        |> spawnForge                    
                    )
                ) 
                |> ignore       
            ()
        )
        
    
    let activate disposables = 
        let watcher = workspace.Globals.createFileSystemWatcher ("**/*.fs")
        watcher.onDidCreate.Add(onFsFileCreateHandler, null, disposables)
        watcher.onDidDelete.Add(onFsFileRemovedHandler, null, disposables)
        commands.Globals.registerCommand("fsharp.MoveFileUp", moveFileUp |> unbox) |> ignore 
        commands.Globals.registerCommand("fsharp.MoveFileDown", moveFileDown |> unbox) |> ignore
        commands.Globals.registerCommand("fsharp.NewProject", newProject |> unbox) |> ignore
        () 
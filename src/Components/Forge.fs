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
        let cmd = (cmd + " --no-prompt")
        let outputChannel = window.Globals.createOutputChannel "Forge"
        outputChannel.clear ()
        outputChannel.append ("forge " + cmd + "\n")

        Process.spawnWithNotification location "mono" cmd outputChannel
        

    let private execForge cmd =
        Process.exec location "mono" (cmd + " --no-prompt")
        
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
            sprintf "move file -n %s -u" editor.document.fileName |> spawnForge |> ignore
    
    let moveFileDown () =
        let editor = vscode.window.Globals.activeTextEditor
        if editor.document.languageId = "fsharp" then
            sprintf "move file -n %s -d" editor.document.fileName |> spawnForge |> ignore
            
    let refreshTemplates () = 
        let cp = "refresh" |> spawnForge
        cp.on("exit", (fun _ ->  window.Globals.showInformationMessage "Templates refreshed") |> unbox )
        
    let addCurrentFileToProject () = 
        let editor = vscode.window.Globals.activeTextEditor
        if editor.document.languageId = "fsharp" then
            sprintf "add file -n %s" editor.document.fileName |> spawnForge |> ignore
            
    let removeCurrentFileFromProject () = 
        let editor = vscode.window.Globals.activeTextEditor
        if editor.document.languageId = "fsharp" then
            sprintf "remove file -n %s" editor.document.fileName |> spawnForge |> ignore

    let addProjectReference () = 
        let projects = Project.getAll () |> List.toArray
        if projects.Length <> 0 then
            projects
            |> Promise.lift
            |> fun n -> 
                let opts = createEmpty<QuickPickOptions>()
                opts.placeHolder <- "Project to edit"
                window.Globals.showQuickPick(n,opts)
                |> Promise.toPromise
                |> Promise.success (fun edit ->
                    let opts = createEmpty<QuickPickOptions>()
                    opts.placeHolder <- "Reference"
                    window.Globals.showQuickPick(n,opts)
                    |> Promise.toPromise
                    |> Promise.success (fun n -> 
                        sprintf "add project -n %s -p %s" n edit |> spawnForge |> ignore
                    ) )
                |> ignore

    let removeProjectReference () =
        let projects = Project.getAll () |> List.toArray
        if projects.Length <> 0 then
            projects
            |> Promise.lift
            |> fun n ->
                let opts = createEmpty<QuickPickOptions>()
                opts.placeHolder <- "Project to edit"
                window.Globals.showQuickPick(n,opts)
                |> Promise.toPromise
                |> Promise.bind (fun edit ->
                    sprintf "list projectReferences -p %s" edit
                    |> execForge
                    |> Promise.success handleForgeList
                    |> Promise.success (fun n ->
                        if n.length <> 0. then
                            let opts = createEmpty<QuickPickOptions>()
                            opts.placeHolder <- "Reference"
                            window.Globals.showQuickPick(n |> Promise.lift,opts)
                            |> Promise.toPromise
                            |> Promise.success (fun ref ->
                                sprintf "remove project -n %s -p %s" ref edit |> spawnForge |> ignore )
                            |> ignore
                    ))
            |> ignore

    let newProject () = 
        "list templates"
        |> execForge
        |> Promise.success handleForgeList
        |> Promise.success (fun n -> 
            if n.length <> 0. then
                window.Globals.showQuickPick (Promise.lift n)
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
                    ())
            else
                window.Globals.showInformationMessage "No templates found. Run `F#: Refresh Project Templates` command"
                |> Promise.toPromise
                |> Promise.success ignore )
        |> Promise.success (fun _ ->
            window.Globals.showInformationMessage "Project created"
        )
        
    
    let activate disposables = 
        let watcher = workspace.Globals.createFileSystemWatcher ("**/*.fs")
        watcher.onDidCreate.Add(onFsFileCreateHandler, null, disposables)
        watcher.onDidDelete.Add(onFsFileRemovedHandler, null, disposables)
        commands.Globals.registerCommand("fsharp.MoveFileUp", moveFileUp |> unbox) |> ignore 
        commands.Globals.registerCommand("fsharp.MoveFileDown", moveFileDown |> unbox) |> ignore
        commands.Globals.registerCommand("fsharp.NewProject", newProject |> unbox) |> ignore
        commands.Globals.registerCommand("fsharp.RefreshProjectTemplates", refreshTemplates |> unbox) |> ignore
        commands.Globals.registerTextEditorCommand("fsharp.AddFileToProject", addCurrentFileToProject |> unbox) |> ignore
        commands.Globals.registerTextEditorCommand("fsharp.RemoveFileFromProject", removeCurrentFileFromProject |> unbox) |> ignore
        commands.Globals.registerCommand("fsharp.AddProjectReference", addProjectReference |> unbox) |> ignore
        commands.Globals.registerCommand("fsharp.RemoveProjectReference", removeProjectReference |> unbox) |> ignore
        () 
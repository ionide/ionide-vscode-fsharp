namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers
module node = Fable.Import.Node.Exports

module Forge =

    let private handleUntitled (fn : string) = if fn.EndsWith ".fs" || fn.EndsWith ".fsi" || fn.EndsWith ".fsx" then fn else (fn + ".fs")

    let (</>) a b =
        if  Process.isWin ()
        then a + @"\" + b
        else a + "/" + b

    let private location =
        let ionidePath = VSCodeExtension.ionidePluginPath ()
        ionidePath </> "bin_forge" </> "Forge.dll"

    let outputChannel = window.createOutputChannel "Forge"

    let private spawnForge (cmd : string) =
        let cmd = cmd.Replace("\r", "").Replace("\n", "")
        let cmd = (cmd + " --no-prompt")
        let cmd = location + " " + cmd
        outputChannel.clear ()
        outputChannel.append (cmd + "\n")

        Process.spawnWithNotification "dotnet" "" cmd outputChannel

    let private execForge cmd =
        Process.exec "dotnet" "" (location + " " + cmd + " --no-prompt")

    let private handleForgeList (error : ChildProcess.ExecError option, stdout : string, stderr : string) =
        if(stdout = "") then
            [||]
        else
            stdout.Split('\n')
            |> Array.filter((<>) "" )
        |> ResizeArray

    let private quotePath (path : string) =
        if JS.isDefined path then
            if path = "" || path.Contains " " then "\"" + path + "\"" else path
        else
            path

    let moveFileUpPath path =
        sprintf "move file -n %s -u" (quotePath path) |> spawnForge |> ignore

    let moveFileDownPath path =
        sprintf "move file -n %s -d" (quotePath path) |> spawnForge |> ignore

    let removeFilePath path =
        sprintf "remove file -n %s" (quotePath path) |> spawnForge |> ignore

    let addFileAbove fromFile project path  =
        sprintf "add file -p %s -n %s --above %s"  (quotePath project) (quotePath path) (quotePath fromFile) |> spawnForge |> ignore

    let addFileBelow fromFile project path =
        sprintf "add file -p %s -n %s --below %s" (quotePath project) (quotePath path) (quotePath fromFile) |> spawnForge |> ignore

    let addFile project path =
        sprintf "add file -p %s -n %s" (quotePath project) (quotePath path) |> spawnForge |> ignore

    let addProjectReferencePath path =
        promise {
            let projects = Project.getAll () |> ResizeArray
            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Reference"
                let! n = window.showQuickPick(projects |> U2.Case1, opts)
                if JS.isDefined n && JS.isDefined path then
                    sprintf "add project -n %s -p %s" (quotePath n) (quotePath path) |> spawnForge |> ignore
        }

    let removeProjectReferencePath ref proj =
        sprintf "remove project -n %s -p %s" (quotePath ref) (quotePath proj) |> spawnForge |> ignore

    let renameFilePath oldName proj =
        promise {
            let fn = node.path.basename oldName
            let dir = node.path.dirname oldName
            let opts = createEmpty<InputBoxOptions>
            opts.value <- Some fn
            let! n = window.showInputBox(opts)
            if JS.isDefined n then
                let newName = node.path.join(dir, n)
                let newName = handleUntitled newName
                sprintf "rename file -n %s -r %s -p %s" (quotePath oldName) (quotePath newName) (quotePath proj) |> spawnForge |> ignore
        }

    let moveFileToFolder (folderList : string list) file proj =
        promise {
            let! _ = sprintf "remove file -n %s" file |> spawnForge |> Process.toPromise
            if folderList.Length <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Reference"
                let! n = window.showQuickPick(folderList |> List.toSeq |> ResizeArray |> U2.Case1, opts) |> Promise.map quotePath
                if JS.isDefined n then
                    let fn = node.path.basename file
                    let projDir = node.path.dirname proj
                    let newFile = node.path.join(projDir, n, fn )
                    node.fs.rename(file, newFile, fun err ->
                        promise {
                            let! _ = sprintf "add file -n %s" (quotePath newFile) |> spawnForge |> Process.toPromise
                            let! _ = sprintf "move file -n %s -d" (quotePath newFile) |> spawnForge |> Process.toPromise
                            return ()
                        } |> ignore
                    )
        }

    let moveFileUp () =
        let editor = vscode.window.activeTextEditor
        match editor.document with
        | Document.FSharp -> sprintf "move file -n %s -u" (quotePath editor.document.fileName) |> spawnForge |> ignore
        | _ -> ()

    let moveFileDown () =
        let editor = vscode.window.activeTextEditor
        match editor.document with
        | Document.FSharp -> sprintf "move file -n %s -d" (quotePath editor.document.fileName) |> spawnForge |> ignore
        | _ -> ()

    let addCurrentFileToProject () =
        let editor = vscode.window.activeTextEditor
        match editor.document with
        | Document.FSharp -> sprintf "add file -n %s" (quotePath editor.document.fileName) |> spawnForge |> ignore
        | _ -> ()

    let removeCurrentFileFromProject () =
        let editor = vscode.window.activeTextEditor
        match editor.document with
        | Document.FSharp -> sprintf "remove file -n %s" (quotePath editor.document.fileName) |> spawnForge |> ignore
        | _ -> ()


    let addProjectReference () =
        promise {
            let projects = Project.getAll () |> ResizeArray
            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Project to edit"
                let! edit = window.showQuickPick(projects |> U2.Case1, opts)

                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Reference"
                let! n = window.showQuickPick(projects |> U2.Case1, opts)
                if JS.isDefined n && JS.isDefined edit then
                    sprintf "add project -n %s -p %s" (quotePath n) (quotePath edit) |> spawnForge |> ignore
        }


    let removeProjectReference () =
        promise {
            let projects = Project.getAll () |> ResizeArray
            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Project to edit"
                let! edit = window.showQuickPick(projects |> U2.Case1,opts)

                let! n =
                    sprintf "list projectReferences -p %s" (quotePath edit)
                    |> execForge
                    |> Promise.map handleForgeList

                if n.Count <> 0 then
                    let opts = createEmpty<QuickPickOptions>
                    opts.placeHolder <- Some "Reference"
                    let! ref = window.showQuickPick(n |> U2.Case1,opts)
                    if JS.isDefined ref && JS.isDefined edit then
                        sprintf "remove project -n %s -p %s" (quotePath ref) (quotePath edit) |> spawnForge |> ignore
        }

    let newProject () =
        promise {
            let! templates = LanguageService.dotnetNewList ()

            let n =
                templates
                |> List.map (fun t ->
                    let res = createEmpty<QuickPickItem>
                    res.label <- t.Name
                    res.description <- t.ShortName
                    res
                ) |> ResizeArray

            let cwd = vscode.workspace.rootPath;
            if JS.isDefined cwd then
                let! template = window.showQuickPick ( n |> U2.Case1)
                if JS.isDefined template then
                    let opts = createEmpty<InputBoxOptions>
                    opts.prompt <- Some "Project directory, relative to workspace root (-o parameter)"
                    let! dir = window.showInputBox (opts)

                    let opts = createEmpty<InputBoxOptions>
                    opts.prompt <- Some "Project name (-n parameter)"
                    let! name =  window.showInputBox(opts)
                    if JS.isDefined dir && JS.isDefined name then
                        let output = if String.IsNullOrWhiteSpace dir then None else Some dir
                        let name = if String.IsNullOrWhiteSpace name then None else Some name

                        let! _ = LanguageService.dotnetNewRun template.description name output
                        ()
            else
                window.showErrorMessage "No open folder." |> ignore
        }


    let activate (context : ExtensionContext) =
        let watcher = workspace.createFileSystemWatcher ("**/*.fs")

        commands.registerCommand("fsharp.MoveFileUp", moveFileUp |> unbox<Func<obj,obj>> ) |> context.subscriptions.Add
        commands.registerCommand("fsharp.MoveFileDown", moveFileDown |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsharp.NewProject", newProject |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerTextEditorCommand("fsharp.AddFileToProject", addCurrentFileToProject |> unbox) |> context.subscriptions.Add
        commands.registerTextEditorCommand("fsharp.RemoveFileFromProject", removeCurrentFileFromProject |> unbox) |> context.subscriptions.Add
        commands.registerCommand("fsharp.AddProjectReference", addProjectReference |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsharp.RemoveProjectReference", removeProjectReference |> unbox<Func<obj,obj>>) |> context.subscriptions.Add

        ()

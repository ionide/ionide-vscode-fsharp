namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers

module Forge =

    type Template = {name : string; value : string}

    type TemplateFile = { Templates : Template[] }

    let (</>) a b =
        if  Process.isWin ()
        then a + @"\" + b
        else a + "/" + b

    let private location =
        try
            (VSCode.getPluginPath "Ionide.ionide-fsharp") </> "bin_forge" </> "Forge.exe"
        with
        | _ -> (VSCode.getPluginPath "Ionide.Ionide-fsharp") </> "bin_forge" </> "Forge.exe"
    let private templateLocation =
        try
            (VSCode.getPluginPath "Ionide.ionide-fsharp") </> "bin_forge" </> "templates" </> "templates.json"
        with
        | _ -> (VSCode.getPluginPath "Ionide.Ionide-fsharp") </> "bin_forge" </> "templates" </> "templates.json"

    let outputChannel = window.createOutputChannel "Forge"

    let private spawnForge (cmd : string) =
        let cmd = cmd.Replace("\r", "").Replace("\n", "")
        let cmd = (cmd + " --no-prompt")
        outputChannel.clear ()
        outputChannel.append ("forge " + cmd + "\n")

        Process.spawnWithNotification location "mono" cmd outputChannel

    let private execForge cmd =
        Process.exec location "mono" (cmd + " --no-prompt")

    let private handleForgeList (error : ChildProcess.ExecError option, stdout : string, stderr : string) =
        if(stdout = "") then
            [||]
        else
            stdout.Split('\n')
            |> Array.filter((<>) "" )
        |> ResizeArray

    let private quotePath (path : string) =
        if JS.isDefined path then
            if path.Contains " " then "\"" + path + "\"" else path
        else
            path
    let moveFileUpPath path =
        sprintf "move file -n %s -u" path |> spawnForge |> ignore

    let moveFileDownPath path =
        sprintf "move file -n %s -d" path |> spawnForge |> ignore

    let removeFilePath path =
        sprintf "remove file -n %s" path |> spawnForge |> ignore

    let addFileAbove fromFile project path  =
        sprintf "add file -p %s -n %s --above %s"  project path fromFile |> spawnForge |> ignore

    let addFileBelow fromFile project path =
        sprintf "add file -p %s -n %s --below %s" project path fromFile |> spawnForge |> ignore

    let addFile project path =
        sprintf "add file -p %s -n %s" project path  |> spawnForge |> ignore

    let addReferencePath path =
        promise {
            let opts = createEmpty<InputBoxOptions>
            opts.placeHolder <- Some "Reference"
            let! name = window.showInputBox(opts) |> Promise.map quotePath
            if JS.isDefined name && JS.isDefined path then
                sprintf "add reference -n %s -p %s" name path |> spawnForge |> ignore }

    let addProjectReferencePath path =
        promise {
            let projects = Project.getAll () |> ResizeArray
            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Reference"
                let! n = window.showQuickPick(projects |> U2.Case1, opts) |> Promise.map quotePath
                if JS.isDefined n && JS.isDefined path then
                    sprintf "add project -n %s -p %s" n path |> spawnForge |> ignore }

    let removeProjectReferencePath ref proj =
        sprintf "remove project -n %s -p %s" ref proj |> spawnForge |> ignore

    let renameFilePath oldName proj =
        promise {
            let fn = Path.basename oldName
            let dir = Path.dirname oldName
            let opts = createEmpty<InputBoxOptions>
            opts.value <- Some fn
            let! n = window.showInputBox(opts)
            if JS.isDefined n then
                let newName = Path.join(dir, n)
                sprintf "rename file -n %s -r %s -p %s" oldName newName proj |> spawnForge |> ignore
        }

    let moveFileToFolder (folderList : string list) file proj =
        promise {
            let! _ = sprintf "remove file -n %s" file |> spawnForge |> Process.toPromise
            if folderList.Length <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Reference"
                let! n = window.showQuickPick(folderList |> List.toSeq |> ResizeArray |> U2.Case1, opts) |> Promise.map quotePath
                if JS.isDefined n then
                    let fn = Path.basename file
                    let projDir = Path.dirname proj
                    let newFile = Path.join(projDir, n, fn )
                    Fs.rename(file, newFile, fun err ->
                        promise {
                            let! _ = sprintf "add file -n %s" newFile |> spawnForge |> Process.toPromise
                            let! _ = sprintf "move file -n %s -d" newFile |> spawnForge |> Process.toPromise
                            return ()
                        } |> ignore
                    )
        }

    let moveFileUp () =
        let editor = vscode.window.activeTextEditor
        match editor.document with
        | Document.FSharp -> sprintf "move file -n %s -u" editor.document.fileName |> spawnForge |> ignore
        | _ -> ()

    let moveFileDown () =
        let editor = vscode.window.activeTextEditor
        match editor.document with
        | Document.FSharp -> sprintf "move file -n %s -d" editor.document.fileName |> spawnForge |> ignore
        | _ -> ()

    let refreshTemplates () =
        "refresh" |> spawnForge |> ignore

    let addCurrentFileToProject () =
        let editor = vscode.window.activeTextEditor
        match editor.document with
        | Document.FSharp -> sprintf "add file -n %s" editor.document.fileName |> spawnForge |> ignore
        | _ -> ()

    let removeCurrentFileFromProject () =
        let editor = vscode.window.activeTextEditor
        match editor.document with
        | Document.FSharp -> sprintf "remove file -n %s" editor.document.fileName |> spawnForge |> ignore
        | _ -> ()

    let addReference () =
        promise {
            let projects = Project.getAll () |> ResizeArray
            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Project to edit"
                let! edit = window.showQuickPick(projects |> U2.Case1,opts) |> Promise.map quotePath

                let opts = createEmpty<InputBoxOptions>
                opts.placeHolder <- Some "Reference"
                let! name = window.showInputBox(opts) |> Promise.map quotePath
                if JS.isDefined name && JS.isDefined edit then
                    sprintf "add reference -n %s -p %s" name edit |> spawnForge |> ignore }

    let removeReference () =
        promise {
            let projects = Project.getAll () |> ResizeArray
            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Project to edit"
                let! edit = window.showQuickPick(projects |> U2.Case1,opts) |> Promise.map quotePath

                let! n =
                    sprintf "list references -p %s" edit
                    |> execForge
                    |> Promise.map handleForgeList

                if n.Count <> 0 then
                    let opts = createEmpty<QuickPickOptions>
                    opts.placeHolder <- Some "Reference"
                    let! ref = window.showQuickPick(n |> U2.Case1,opts) |> Promise.map quotePath
                    if JS.isDefined ref && JS.isDefined edit then
                        sprintf "remove reference -n %s -p %s" ref edit |> spawnForge |> ignore }


    let addProjectReference () =
        promise {
            let projects = Project.getAll () |> ResizeArray
            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Project to edit"
                let! edit = window.showQuickPick(projects |> U2.Case1, opts) |> Promise.map quotePath

                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Reference"
                let! n = window.showQuickPick(projects |> U2.Case1, opts) |> Promise.map quotePath
                if JS.isDefined n && JS.isDefined edit then
                    sprintf "add project -n %s -p %s" n edit |> spawnForge |> ignore }


    let removeProjectReference () =
        promise {
            let projects = Project.getAll () |> ResizeArray
            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Project to edit"
                let! edit = window.showQuickPick(projects |> U2.Case1,opts) |> Promise.map quotePath

                let! n =
                    sprintf "list projectReferences -p %s" edit
                    |> execForge
                    |> Promise.map handleForgeList

                if n.Count <> 0 then
                    let opts = createEmpty<QuickPickOptions>
                    opts.placeHolder <- Some "Reference"
                    let! ref = window.showQuickPick(n |> U2.Case1,opts) |> Promise.map quotePath
                    if JS.isDefined ref && JS.isDefined edit then
                        sprintf "remove project -n %s -p %s" ref edit |> spawnForge |> ignore }

    let private logger = ConsoleAndOutputChannelLogger(Some "Forge", Level.DEBUG, None, Some Level.DEBUG)

    let newProject () =
        promise {
            if Fs.existsSync (U2.Case1 templateLocation) then
                let f = (Fs.readFileSync templateLocation).toString()
                let file : TemplateFile = f |> JS.JSON.parse |> unbox

                let n =
                    file.Templates
                    |> Array.map (fun t ->
                        let res = createEmpty<QuickPickItem>
                        res.label <- t.value
                        res.description <- t.name
                        res
                    ) |> ResizeArray

                if n.Count <> 0 then
                    let cwd = vscode.workspace.rootPath;
                    if JS.isDefined cwd then
                        let! template = window.showQuickPick ( n |> U2.Case1)
                        if JS.isDefined template then
                            let opts = createEmpty<InputBoxOptions>
                            opts.prompt <- Some "Project directory"
                            let! dir = window.showInputBox (opts)

                            let opts = createEmpty<InputBoxOptions>
                            opts.prompt <- Some "Project name"
                            let! name =  window.showInputBox(opts)
                            if JS.isDefined dir && JS.isDefined name then
                                if name <> "" then
                                    let msg = window.setStatusBarMessage "Creating project..."
                                    sprintf """new project -n "%s" -t %s --folder "%s" """ name template.label dir
                                    |> spawnForge
                                    |> Process.toPromise
                                    |> Promise.bind (fun _ ->
                                        msg.dispose() |> ignore
                                        window.showInformationMessage "Project created"
                                    )
                                    |> ignore
                                else
                                    window.showErrorMessage "Invalid project name." |> ignore
                    else
                        window.showErrorMessage "No open folder." |> ignore
                else
                    window.showInformationMessage "No templates found. Run `F#: Refresh Project Templates` command" |> ignore
            else
                window.showInformationMessage "No templates found. Run `F#: Refresh Project Templates` command" |> ignore
        }

    let newProjectNoFake () =
        promise {
            if Fs.existsSync (U2.Case1 templateLocation) then
                let f = (Fs.readFileSync templateLocation).toString()
                let file : TemplateFile = f |> JS.JSON.parse |> unbox

                let n =
                    file.Templates
                    |> Array.map (fun t ->
                        let res = createEmpty<QuickPickItem>
                        res.label <- t.value
                        res.description <- t.name
                        res
                    ) |> ResizeArray


                if n.Count <> 0 then
                    let! template = window.showQuickPick ( n |> U2.Case1)
                    if JS.isDefined template then
                        let opts = createEmpty<InputBoxOptions>
                        opts.prompt <- Some "Project directory"
                        let! dir = window.showInputBox (opts)

                        let opts = createEmpty<InputBoxOptions>
                        opts.prompt <- Some "Project name"
                        let! name =  window.showInputBox(opts)
                        if JS.isDefined dir && JS.isDefined name then
                            let msg = window.setStatusBarMessage "Creating project..."
                            sprintf "new project -n %s -t %s --folder %s --no-fake" name template.label dir
                            |> spawnForge
                            |> Process.toPromise
                            |> Promise.bind (fun _ ->
                                msg.dispose() |> ignore
                                window.showInformationMessage "Project created"
                            )
                            |> ignore
                else
                    window.showInformationMessage "No templates found. Run `F#: Refresh Project Templates` command" |> ignore
            else
                window.showInformationMessage "No templates found. Run `F#: Refresh Project Templates` command" |> ignore
        }

    let newProjectScaffold () =
        promise {
            let msg = window.setStatusBarMessage "Creating project..."
            sprintf "new scaffold"
            |> spawnForge
            |> Process.toPromise
            |> Promise.bind (fun _ ->
                msg.dispose() |> ignore
                window.showInformationMessage "Project created"
            )
            |> ignore

        }



    let activate (context: ExtensionContext) =
        let watcher = workspace.createFileSystemWatcher ("**/*.fs")

        commands.registerCommand("fsharp.MoveFileUp", moveFileUp |> unbox<Func<obj,obj>> ) |> context.subscriptions.Add
        commands.registerCommand("fsharp.MoveFileDown", moveFileDown |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsharp.NewProject", newProject |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsharp.NewProjectNoFake", newProjectNoFake |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsharp.NewProjectScaffold", newProjectScaffold |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsharp.RefreshProjectTemplates", refreshTemplates |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerTextEditorCommand("fsharp.AddFileToProject", addCurrentFileToProject |> unbox) |> context.subscriptions.Add
        commands.registerTextEditorCommand("fsharp.RemoveFileFromProject", removeCurrentFileFromProject |> unbox) |> context.subscriptions.Add
        commands.registerCommand("fsharp.AddProjectReference", addProjectReference |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsharp.RemoveProjectReference", removeProjectReference |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsharp.AddReference", addReference |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        commands.registerCommand("fsharp.RemoveReference", removeReference |> unbox<Func<obj,obj>>) |> context.subscriptions.Add
        if Fs.existsSync (U2.Case1 templateLocation) |> not then refreshTemplates ()

        ()

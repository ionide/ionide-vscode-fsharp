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

    type Template =
        { name : string
          value : string }

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

    let addReferencePath path =
        promise {
            let opts = createEmpty<InputBoxOptions>
            opts.placeHolder <- Some "Reference"
            let! name = window.showInputBox(opts)
            if JS.isDefined name && JS.isDefined path then
                sprintf "add reference -n %s -p %s" (quotePath name) (quotePath path) |> spawnForge |> ignore
        }

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

    let refreshTemplates () =
        "refresh" |> spawnForge |> ignore

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

    let addReference () =
        promise {
            let projects = Project.getAll () |> ResizeArray
            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Project to edit"
                let! edit = window.showQuickPick(projects |> U2.Case1,opts)

                let opts = createEmpty<InputBoxOptions>
                opts.placeHolder <- Some "Reference"
                let! name = window.showInputBox(opts)
                if JS.isDefined name && JS.isDefined edit then
                    sprintf "add reference -n %s -p %s" (quotePath name) (quotePath edit) |> spawnForge |> ignore
        }

    let removeReference () =
        promise {
            let projects = Project.getAll () |> ResizeArray
            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Project to edit"
                let! edit = window.showQuickPick(projects |> U2.Case1,opts)

                let! n =
                    sprintf "list references -p %s" (quotePath edit)
                    |> execForge
                    |> Promise.map handleForgeList

                if n.Count <> 0 then
                    let opts = createEmpty<QuickPickOptions>
                    opts.placeHolder <- Some "Reference"
                    let! ref = window.showQuickPick(n |> U2.Case1,opts)
                    if JS.isDefined ref && JS.isDefined edit then
                        sprintf "remove reference -n %s -p %s" (quotePath ref) (quotePath edit) |> spawnForge |> ignore
        }


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

    let private logger = ConsoleAndOutputChannelLogger(Some "Forge", Level.DEBUG, None, Some Level.DEBUG)

    let newProject () =
        promise {
            if node.fs.existsSync (U2.Case1 templateLocation) then
                let f = (node.fs.readFileSync templateLocation).toString()
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
                                    sprintf """new project -n %s -t %s --folder %s """ (quotePath name) template.label (quotePath dir)
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
            if node.fs.existsSync (U2.Case1 templateLocation) then
                let f = (node.fs.readFileSync templateLocation).toString()
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
                            sprintf "new project -n %s -t %s --folder %s --no-fake" (quotePath name) template.label (quotePath dir)
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
            let mutable output = ""
            sprintf "new scaffold"
            |> spawnForge
            |> Process.onOutput (fun f ->
                output <- output + f.toString()
            )
            |> Process.onErrorOutput (fun f ->
                output <- output + f.toString()
            )
            |> Process.toPromise
            |> Promise.bind (fun _ ->
                msg.dispose() |> ignore
                if output.Contains "fatal: destination path" && output.Contains "already exists and is not an empty directory" then
                    window.showInformationMessage "Creating project failed - directory is not empty"
                else
                    window.showInformationMessage "Project created"
            )
            |> ignore
        }


    let activate (context : ExtensionContext) =
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
        if node.fs.existsSync (U2.Case1 templateLocation) |> not then refreshTemplates ()

        ()

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

    let private location = (VSCode.getPluginPath "Ionide.Ionide-fsharp") </> "bin_forge" </> "Forge.exe"
    let private templateLocation = (VSCode.getPluginPath "Ionide.Ionide-fsharp") </> "templates" </> "templates.json"
    let outputChannel = window.createOutputChannel "Forge"

    let private spawnForge (cmd : string) =
        let cmd = cmd.Replace("\r", "").Replace("\n", "")
        let cmd = (cmd + " --no-prompt")
        outputChannel.clear ()
        outputChannel.append ("forge " + cmd + "\n")

        Process.spawnWithNotification location "mono" cmd outputChannel


    let private execForge cmd =
        Process.exec location "mono" (cmd + " --no-prompt")

    let private handleForgeList (error : Node.Error, stdout : Buffer, stderr : Buffer) =
        if(stdout.toString() = "") then
            [||]
        else
            stdout.toString().Split('\n')
            |> Array.filter((<>) "" )
        |> ResizeArray

    let onFsFileCreateHandler (uri : Uri) =
        if "FSharp.automaticProjectModification" |> Configuration.get false then sprintf "add file -n %s" uri.fsPath |> spawnForge |> ignore

    let onFsFileRemovedHandler (uri : Uri) =
        if "FSharp.automaticProjectModification" |> Configuration.get false then sprintf "remove file -n %s" uri.fsPath |> spawnForge |> ignore

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
                let! edit = window.showQuickPick(projects |> Case1,opts)

                let opts = createEmpty<InputBoxOptions>
                opts.placeHolder <- Some "Reference"
                let! name = window.showInputBox(opts)
                if JS.isDefined name && JS.isDefined edit then
                    sprintf "add reference -n %s -p %s" name edit |> spawnForge |> ignore }

    let removeReference () =
        promise {
            let projects = Project.getAll () |> ResizeArray
            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Project to edit"
                let! edit = window.showQuickPick(projects |> Case1,opts)

                let! n =
                    sprintf "list references -p %s" edit
                    |> execForge
                    |> Promise.map handleForgeList

                if n.Count <> 0 then
                    let opts = createEmpty<QuickPickOptions>
                    opts.placeHolder <- Some "Reference"
                    let! ref = window.showQuickPick(n |> Case1,opts)
                    if JS.isDefined ref && JS.isDefined edit then
                        sprintf "remove reference -n %s -p %s" ref edit |> spawnForge |> ignore }


    let addProjectReference () =
        promise {
            let projects = Project.getAll () |> ResizeArray
            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Project to edit"
                let! edit = window.showQuickPick(projects |> Case1, opts)

                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Reference"
                let! n = window.showQuickPick(projects |> Case1, opts)
                if JS.isDefined n && JS.isDefined edit then
                    sprintf "add project -n %s -p %s" n edit |> spawnForge |> ignore }


    let removeProjectReference () =
        promise {
            let projects = Project.getAll () |> ResizeArray
            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Project to edit"
                let! edit = window.showQuickPick(projects |> Case1,opts)

                let! n =
                    sprintf "list projectReferences -p %s" edit
                    |> execForge
                    |> Promise.map handleForgeList

                if n.Count <> 0 then
                    let opts = createEmpty<QuickPickOptions>
                    opts.placeHolder <- Some "Reference"
                    let! ref = window.showQuickPick(n |> Case1,opts)
                    if JS.isDefined ref && JS.isDefined edit then
                        sprintf "remove project -n %s -p %s" ref edit |> spawnForge |> ignore }


    let newProject () =
        promise {
            if fs.existsSync templateLocation then
                let f = (fs.readFileSync templateLocation).ToString()
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
                    let! template = window.showQuickPick ( n |> Case1)
                    if JS.isDefined template then
                        let opts = createEmpty<InputBoxOptions>
                        opts.prompt <- Some "Project directory"
                        let! dir = window.showInputBox (opts)

                        let opts = createEmpty<InputBoxOptions>
                        opts.prompt <- Some "Project name"
                        let! name =  window.showInputBox(opts)
                        if JS.isDefined dir && JS.isDefined name then
                            sprintf "new project -n %s -t %s --folder %s" name template.label dir |> spawnForge |> ignore

                            window.showInformationMessage "Project created"
                            |> ignore
                else
                    window.showInformationMessage "No templates found. Run `F#: Refresh Project Templates` command" |> ignore
            else
                window.showInformationMessage "No templates found. Run `F#: Refresh Project Templates` command" |> ignore
        }

    let newProjectNoFake () =
        promise {
            if fs.existsSync templateLocation then
                let f = (fs.readFileSync templateLocation).ToString()
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
                    let! template = window.showQuickPick ( n |> Case1)
                    if JS.isDefined template then
                        let opts = createEmpty<InputBoxOptions>
                        opts.prompt <- Some "Project directory"
                        let! dir = window.showInputBox (opts)

                        let opts = createEmpty<InputBoxOptions>
                        opts.prompt <- Some "Project name"
                        let! name =  window.showInputBox(opts)
                        if JS.isDefined dir && JS.isDefined name then
                            sprintf "new project -n %s -t %s --folder %s --no-fake" name template.label dir |> spawnForge |> ignore

                            window.showInformationMessage "Project created"
                            |> ignore
                else
                    window.showInformationMessage "No templates found. Run `F#: Refresh Project Templates` command" |> ignore
            else
                window.showInformationMessage "No templates found. Run `F#: Refresh Project Templates` command" |> ignore
        }

    let newProjectScaffold () =
        promise {
            sprintf "new scaffold" |> spawnForge |> ignore
            window.showInformationMessage "Project created" |> ignore

        }



    let activate disposables =
        let watcher = workspace.createFileSystemWatcher ("**/*.fs")

        watcher.onDidCreate $ (onFsFileCreateHandler, null, disposables) |> ignore
        watcher.onDidDelete $ (onFsFileRemovedHandler, null, disposables) |> ignore
        commands.registerCommand("fsharp.MoveFileUp", moveFileUp |> unbox<Func<obj,obj>> ) |> ignore
        commands.registerCommand("fsharp.MoveFileDown", moveFileDown |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsharp.NewProject", newProject |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsharp.NewProjectNoFake", newProjectNoFake |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsharp.NewProjectScaffold", newProjectScaffold |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsharp.RefreshProjectTemplates", refreshTemplates |> unbox<Func<obj,obj>>) |> ignore
        commands.registerTextEditorCommand("fsharp.AddFileToProject", addCurrentFileToProject |> unbox) |> ignore
        commands.registerTextEditorCommand("fsharp.RemoveFileFromProject", removeCurrentFileFromProject |> unbox) |> ignore
        commands.registerCommand("fsharp.AddProjectReference", addProjectReference |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsharp.RemoveProjectReference", removeProjectReference |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsharp.AddReference", addReference |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("fsharp.RemoveReference", removeReference |> unbox<Func<obj,obj>>) |> ignore
        if fs.existsSync templateLocation |> not then refreshTemplates () |> ignore

        ()

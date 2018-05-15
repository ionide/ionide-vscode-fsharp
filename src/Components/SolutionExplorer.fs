namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers
open System.Collections.Generic

open DTO

module SolutionExplorer =
    type Model =
        | Workspace of Projects : Model list
        | Solution of parent: Model option ref * path: string * name: string * items: Model list
        | WorkspaceFolder of parent: Model option ref * name: string * items: Model list
        | ReferenceList of parent: Model option ref * References: Model list * projectPath : string
        | ProjectReferencesList of parent: Model option ref * Projects : Model list * ProjectPath : string
        | ProjectNotLoaded of parent: Model option ref * path: string * name: string
        | ProjectLoading of parent: Model option ref * path: string * name: string
        | ProjectFailedToLoad of parent: Model option ref * path: string * name: string * error: string
        | ProjectNotRestored of parent: Model option ref * path: string * name: string * error: string
        | Project of parent: Model option ref * path: string * name: string * Files: Model list * ProjectReferencesList : Model  * ReferenceList: Model * isExe : bool * project : DTO.Project
        | Folder of parent: Model option ref * name : string * path: string * Files : Model list * projectPath : string
        | File of parent: Model option ref * path: string * name: string * projectPath : string
        | Reference of parent: Model option ref * path: string * name: string * projectPath : string
        | ProjectReference of parent: Model option ref * path: string * name: string * projectPath : string

    type NodeEntry = {
        Key : string
        Children : Dictionary<string, NodeEntry>
    }

    let rec add' (state : NodeEntry) (entry : string) index =
        let sep = Path.sep

        if index >= entry.Length then
            state
        else
            let endIndex = entry.IndexOf(sep, index)
            let endIndex = if endIndex = -1 then entry.Length else endIndex

            let key = entry.Substring(index, endIndex - index)
            if String.IsNullOrEmpty key then
                state
            else
                if state.Children.ContainsKey key |> not then
                    let x = {Key = key; Children = new Dictionary<_,_>()}
                    state.Children.Add(key,x)
                let item = state.Children.[key]
                add' item entry (endIndex + 1)

    let private getParentRef (model: Model) =
        match model with
        | Workspace _ -> ref None
        | Solution (parent, _, _, _) -> parent
        | WorkspaceFolder (parent, _, _) -> parent
        | ReferenceList (parent, _, _) -> parent
        | ProjectReferencesList (parent, _, _) -> parent
        | ProjectNotLoaded (parent, _, _) -> parent
        | ProjectLoading (parent, _, _) -> parent
        | ProjectFailedToLoad (parent, _, _, _) -> parent
        | ProjectNotRestored (parent, _, _, _) -> parent
        | Project (parent, _, _, _, _, _, _, _) -> parent
        | Folder (parent, _, _, _, _) -> parent
        | File (parent, _, _, _) -> parent
        | Reference (parent, _, _, _) -> parent
        | ProjectReference (parent, _, _, _) -> parent

    let inline private setParentRef (model: Model) (parent: Model) =
        let parentRef = getParentRef model
        parentRef := Some parent

    let private setParentRefs (models: #seq<Model>) (parent: Model) =
        for model in models do
            setParentRef model parent

    let rec toModel folder pp (entry : NodeEntry)  =
        let f = (folder + Path.sep + entry.Key)
        if entry.Children.Count > 0 then
            let childs =
                entry.Children
                |> Seq.map (fun n -> toModel f pp n.Value )
                |> Seq.toList
            let result = Folder(ref None, entry.Key, f, childs, pp)
            setParentRefs childs result
            result
        else
            let p = (Path.dirname pp) + f
            File(ref None, p, entry.Key, pp)

    let buildTree pp (files : string list) =
        let entry = {Key = ""; Children = new Dictionary<_,_>()}
        files |> List.iter (fun x -> add' entry x 0 |> ignore )
        entry.Children
        |> Seq.map (fun n -> toModel "" pp n.Value )
        |> Seq.toList

    let private getProjectModel proj =
        let projects =
            Project.getLoaded ()
            |> Seq.toArray

        let files =
            proj.Files
            |> List.map (fun p -> Path.relative(Path.dirname proj.Project, p))
            |> buildTree proj.Project

        let refs =
            proj.References
            |> List.map (fun p -> Reference(ref None, p,Path.basename p, proj.Project))
            |> fun n ->
                let result = ReferenceList(ref None, n, proj.Project)
                setParentRefs n result
                result

        let projs =
            proj.References
            |> List.choose (fun r ->
                projects
                |> Array.tryFind (fun pr -> pr.Output = r))
            |> List.map (fun p -> ProjectReference(ref None, p.Project, Path.basename(p.Project, ".fsproj"), proj.Project))
            |> fun n ->
                let result = ProjectReferencesList(ref None, n, proj.Project)
                setParentRefs n result
                result

        let name = Path.basename(proj.Project, ".fsproj")
        let result = Project(ref None, proj.Project, name,files, projs, refs, Project.isExeProject proj, proj)
        setParentRefs files result
        setParentRef refs result
        setParentRef projs result
        result

    let private getProjectModelByState proj =
        match proj with
        | Project.ProjectLoadingState.Loading p ->
            Model.ProjectLoading (ref None, p, Path.basename p)
        | Project.ProjectLoadingState.Loaded proj ->
            getProjectModel proj
        | Project.ProjectLoadingState.Failed (p, err) ->
            Model.ProjectFailedToLoad (ref None, p, Path.basename p, err)
        | Project.ProjectLoadingState.NotRestored (p, err) ->
            Model.ProjectNotRestored (ref None, p, Path.basename p, err)

    let getFolders model =
        let rec loop model lst =
            match model with
            | Workspace fls -> fls |> List.collect (fun x -> loop x lst )
            | Project (_, _, _, fls, _, _, _, _) -> fls |> List.collect (fun x -> loop x lst )
            | Folder (_, _, f, fls, _) ->
                fls |> List.collect (fun x -> loop x lst@[f] )
            | _ -> []
        let lst =
            loop model []
            |> List.distinct
            |> List.map (fun n -> n.TrimStart('\\'))
            |> List.sort

        "."::lst

    let private getSolutionModel (ws: WorkspacePeekFound) : Model =
        let getProjItem projPath =
            match Project.tryFindInWorkspace projPath with
            | None ->
                Model.ProjectNotLoaded (ref None, projPath, (Path.basename projPath))
            | Some p ->
                getProjectModelByState p

        let rec getItem (item: WorkspacePeekFoundSolutionItem) =
            match item.Kind with
            | WorkspacePeekFoundSolutionItemKind.Folder folder ->
                let files =
                    folder.Files
                    |> Array.map (fun f -> Model.File (ref None, f,Path.basename(f),""))
                let items = folder.Items |> Array.map getItem
                let result = Model.WorkspaceFolder (ref None, item.Name, (Seq.append files items |> List.ofSeq))
                setParentRefs files result
                setParentRefs items result
                result
            | MsbuildFormat _proj ->
                getProjItem item.Name

        match ws with
        | WorkspacePeekFound.Solution sln ->
            let s = Solution (ref None, sln.Path, (Path.basename sln.Path), (sln.Items |> Array.map getItem |> List.ofArray))
            let result = Workspace [s]
            setParentRef s result
            result
        | WorkspacePeekFound.Directory dir ->
            let items = dir.Fsprojs |> Array.map getProjItem |> List.ofArray
            let result = Workspace items
            setParentRefs items result
            result

    let private getSolution () =
        Project.getLoadedSolution ()
        |> Option.map getSolutionModel

    let private getSubmodel node =
        match node with
        | Workspace projects -> projects
        | WorkspaceFolder (_, _, items) -> items
        | Solution (_, _,_, items) -> items
        | ProjectNotLoaded _ -> []
        | ProjectLoading _ -> []
        | ProjectFailedToLoad _ -> []
        | ProjectNotRestored _ -> []
        | Project (_, _, _, files, projs, refs, _, _) ->
            [
                 // SHOULD REFS BE DISPLAYED AT ALL? THOSE ARE RESOLVED BY MSBUILD REFS
                yield refs
                yield projs
                yield! files
            ]
        | ReferenceList (_, refs, _) -> refs
        | ProjectReferencesList (_, refs, _) -> refs
        | Folder (_, _,_,files, _) -> files
        | File _ -> []
        | Reference _ -> []
        | ProjectReference _ -> []

    let private getLabel node =
        match node with
        | Workspace _ -> "Workspace"
        | WorkspaceFolder (_, name,_) -> name
        | Solution (_, _, name, _) -> name
        | ProjectNotLoaded (_, _, name) -> sprintf "%s (not loaded yet)" name
        | ProjectLoading (_, _, name) -> sprintf "%s (loading..)" name
        | ProjectFailedToLoad (_, _, name, _) -> sprintf "%s (load failed)" name
        | ProjectNotRestored (_, _, name, _) -> sprintf "%s (not restored)" name
        | Project (_, _, name,_, _,_, _, _) -> name
        | ReferenceList _ -> "References"
        | ProjectReferencesList (_, refs, _) -> "Project References"
        | Folder (_, n,_, _, _) -> n
        | File (_, _, name, _) -> name
        | Reference (_, _, name, _) ->
            if name.ToLowerInvariant().EndsWith(".dll") then
                name.Substring(0, name.Length - 4)
            else
                name
        | ProjectReference (_, _, name, _) -> name

    let private getRoot () =
        defaultArg (getSolution ()) (Workspace [])

    let private createProvider (event: Event<Model option>) (rootChanged: EventEmitter<Model>) : TreeDataProvider<Model> =
        let plugPath =
            try
                (VSCode.getPluginPath "Ionide.ionide-fsharp")
            with
            | _ ->  (VSCode.getPluginPath "Ionide.Ionide-fsharp")

        { new TreeDataProvider<Model>
          with
            member __.getParent =
                Some (fun node ->
                    printfn "GET PARENT %A" (getLabel node)
                    if JS.isDefined node then
                        let parentRef = getParentRef node
                        printfn "PARENT = %A" (!parentRef |> Option.map getLabel)
                        !parentRef
                    else
                        None)

            member this.onDidChangeTreeData =
                event

            member this.getChildren(node) =
                if JS.isDefined node then
                    let r = getSubmodel node |> ResizeArray
                    r
                else
                    let root = getRoot()
                    rootChanged.fire root
                    let r = root |> getSubmodel |> ResizeArray
                    r

            member this.getTreeItem(node) =
                let collaps =
                    match node with
                    | File _ | Reference _ | ProjectReference _ ->
                        vscode.TreeItemCollapsibleState.None
                    | ProjectFailedToLoad _ | ProjectLoading _ | ProjectNotLoaded _ | ProjectNotRestored _
                    | Workspace _ | Solution _ ->
                        vscode.TreeItemCollapsibleState.Expanded
                    | WorkspaceFolder (_, _, items) ->
                        let isProj model =
                            match model with
                            | ProjectNotLoaded _ -> true
                            | ProjectLoading _ -> true
                            | Project _ -> true
                            | ProjectFailedToLoad _ -> true
                            | ProjectNotRestored _ -> true
                            | _ -> false
                        // expand workspace folder if contains a project
                        if items |> List.exists isProj then
                            vscode.TreeItemCollapsibleState.Expanded
                        else
                            vscode.TreeItemCollapsibleState.Collapsed
                    | Folder _ | Project _ | ProjectReferencesList _ | ReferenceList _ ->
                        vscode.TreeItemCollapsibleState.Collapsed

                let ti = new TreeItem(getLabel node, collaps)

                let command =
                    match node with
                    | File (_, p, _, _)  ->
                        let c = createEmpty<Command>
                        c.command <- "vscode.open"
                        c.title <- "open"
                        c.arguments <- Some (ResizeArray [| unbox (Uri.file p) |])
                        Some c
                    | _ -> None
                ti.command <- command

                let context =
                    match node with
                    | File _  -> "file"
                    | ProjectReferencesList _  -> "projectRefList"
                    | ReferenceList _  -> "referencesList"
                    | Project (_, _, _, _, _, _, false, _)  -> "project"
                    | Project (_, _, _, _, _, _, true, _)  -> "projectExe"
                    | ProjectReference _  -> "projectRef"
                    | Reference _  -> "reference"
                    | Folder _ -> "folder"
                    | ProjectFailedToLoad _ -> "projectLoadFailed"
                    | ProjectLoading _ -> "projectLoading"
                    | ProjectNotLoaded _ -> "projectNotLoaded"
                    | ProjectNotRestored _ -> "projectNotRestored"
                    | Solution _ -> "solution"
                    | Workspace _ -> "workspace"
                    | WorkspaceFolder _ -> "workspaceFolder"

                ti.contextValue <- Some (sprintf "ionide.projectExplorer.%s" context)

                let p = createEmpty<TreeIconPath>

                let icon, resourceUri =
                    match node with
                    | File (_, path, _, _)
                    | Project (_, path, _, _, _, _, _, _)
                    | ProjectNotLoaded (_, path, _)
                    | ProjectLoading (_, path, _)
                    | ProjectFailedToLoad (_, path, _, _)
                    | ProjectNotRestored (_, path, _, _)
                    | Solution (_, path, _, _)  ->
                        ThemeIcon.File |> U4.Case4 |> Some, Uri.file path |> Some
                    | Folder (_, _, path, _,_) ->
                        ThemeIcon.Folder |> U4.Case4 |> Some, Uri.file path |> Some
                    | WorkspaceFolder _  ->
                        ThemeIcon.Folder |> U4.Case4 |> Some, None
                    | Reference (_, path, _, _) | ProjectReference (_, path, _, _) ->
                        p.light <- (plugPath + "/images/circuit-board-light.svg") |> U3.Case1
                        p.dark <- (plugPath + "/images/circuit-board-dark.svg") |> U3.Case1
                        p |> U4.Case3 |> Some, Uri.file path |> Some
                    | Workspace _
                    | ReferenceList _
                    | ProjectReferencesList _ ->
                        None, None
                ti.iconPath <- icon
                ti.resourceUri <- resourceUri
                ti.id <-
                    match node with
                    | ReferenceList (_, _, pp) | ProjectReferencesList (_, _,pp) | Reference (_, _, _, pp) | ProjectReference (_, _, _, pp)  ->
                        Some ((defaultArg ti.label "") + "||" + pp)
                    | Folder (_, _,_, _, pp) | File (_, _, _, pp) ->
                        (resourceUri |> Option.map(fun u -> (defaultArg ti.label "") + "||" + u.toString() + "||" + pp))
                    | _ ->
                        (resourceUri |> Option.map(fun u -> (defaultArg ti.label "") + "||" + u.toString()))
                ti
        }

    module private ShowInActivity =
        let private setInFsharpActivity = Context.cachedSetter<bool> "fsharp.showProjectExplorerInFsharpActivity"
        let private setInExplorerActivity = Context.cachedSetter<bool> "fsharp.showProjectExplorerInExplorerActivity"

        let initializeAndGetId (): string =
            let showIn = "FSharp.showProjectExplorerIn" |> Configuration.get "fsharp"
            let inFsharpActivity = (showIn = "fsharp")
            setInFsharpActivity inFsharpActivity
            setInExplorerActivity (not inFsharpActivity)

            if inFsharpActivity then "ionide.projectExplorerInActivity" else "ionide.projectExplorer"

    module AutoReveal =
        type private State = {
            RootModel: Model
            ModelPerFile: Map<string, Model>
        }

        let private onDidChangeActiveTextEditor (tree: TreeView<Model>) (state: State option ref) (textEditor: TextEditor) =
            if JS.isDefined textEditor then
                let uri = textEditor.document.uri
                if uri.scheme = "file" && JS.isDefined uri.fsPath then
                    let path = uri.fsPath
                    let model = !state |> Option.bind(fun s -> s.ModelPerFile |> Map.tryFind path)
                    printfn "FOUND %A for %s" model path
                    match model with
                    | Some model ->
                        let options = createEmpty<TreeViewRevealOptions>
                        options.select <- Some false
                        tree.reveal(model, options) |> ignore
                    | _ -> ()

        let rec private getModelPerFile (model: Model) : (string * Model) list =
            match model with
            | File (_, path, _, _)
            | ProjectNotLoaded (_, path, _)
            | ProjectLoading (_, path, _)
            | ProjectFailedToLoad (_, path, _, _)
            | ProjectNotRestored (_, path, _, _) ->
                [ path, model ]
            | Project (_, path, _, children, _, _, _, _)
            | Solution (_, path, _, children) ->
                let current = path, model
                let forChildren = children |> List.collect getModelPerFile
                current :: forChildren
            | Folder (_, _, _, children,_)
            | WorkspaceFolder (_, _, children)
            | Workspace children ->
                children |> List.collect getModelPerFile
            | Reference _
            | ProjectReference _
            | ReferenceList _
            | ProjectReferencesList _->
                []

        let private onEvent (state: State option ref) (newValue: Model) =
            let modelPerFile = getModelPerFile newValue |> Map.ofList
            printfn "------------------------------"
            printfn "UPDATED STATE = %A" modelPerFile
            printfn "------------------------------"
            let newState = Some { RootModel = newValue; ModelPerFile = modelPerFile }
            state := newState

        let activate (context: ExtensionContext) (rootChanged: Event<Model>) (treeView: TreeView<Model>) =
            let state: State option ref = ref None

            let onDidChangeActiveTextEditor = onDidChangeActiveTextEditor treeView state
            window.onDidChangeActiveTextEditor.Invoke(unbox onDidChangeActiveTextEditor)
                |> context.subscriptions.Add

            let onEvent = onEvent state
            rootChanged.Invoke(unbox onEvent)
                |> context.subscriptions.Add

    let activate (context: ExtensionContext) =
        let emiter = EventEmitter<Model option>()
        let rootChanged = EventEmitter<Model>()

        let provider = createProvider emiter.event rootChanged

        let treeViewId = ShowInActivity.initializeAndGetId ()

        Project.workspaceChanged.event.Invoke(fun _ ->
            emiter.fire (undefined) |> unbox)
        |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.refresh", Func<obj, obj>(fun _ ->
            emiter.fire (undefined) |> unbox
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.clearCache", Func<obj, obj>(fun _ ->
            Project.clearCache ()
            |> unbox
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.msbuild.pickHost", Func<obj, obj>(fun _ ->
            MSBuild.pickMSbuildHostType ()
            |> unbox
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.moveUp", Func<obj, obj>(fun m ->
            match unbox m with
            | File (_, p, _, _) ->
                Forge.moveFileUpPath p
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.moveDown", Func<obj, obj>(fun m ->
            match unbox m with
            | File (_, p, _, _) ->
                Forge.moveFileDownPath p
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.moveToFolder", Func<obj, obj>(fun m ->
            let folders =
                getRoot()
                |> getFolders

            match unbox m with
            | File (_, p, _, pp) ->
                Forge.moveFileToFolder folders p pp
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.removeFile", Func<obj, obj>(fun m ->
            match unbox m with
            | File (_, p, _, _) ->
                Forge.removeFilePath p
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.renameFile", Func<obj, obj>(fun m ->
            match unbox m with
            | File (_, old, _, proj) ->
                Forge.renameFilePath old proj
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.addAbove", Func<obj, obj>(fun m ->
            match unbox m with
            | File (_, from, name, proj) ->
                let opts = createEmpty<InputBoxOptions>
                opts.placeHolder <- Some "new.fs"
                opts.prompt <- Some "New file name, relative to project file"
                opts.value <- Some "new.fs"
                window.showInputBox(opts)
                |> Promise.map (fun file ->
                    if JS.isDefined file then
                        let file' = Path.join(proj |> Path.dirname, file)
                        let from = Path.relative(proj |> Path.dirname, from)
                        let proj = Path.relative(workspace.rootPath, proj)
                        Fs.appendFileSync( file', "") |> unbox
                        Forge.addFileAbove from proj file'
                )
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.addBelow", Func<obj, obj>(fun m ->
            match unbox m with
            | File (_, from, name, proj) ->
                let opts = createEmpty<InputBoxOptions>
                opts.placeHolder <- Some "new.fs"
                opts.prompt <- Some "New file name, relative to project file"
                opts.value <- Some "new.fs"
                window.showInputBox(opts)
                |> Promise.map (fun file ->
                    if JS.isDefined file then
                        let file' = Path.join(proj |> Path.dirname, file)
                        let from = Path.relative(proj |> Path.dirname, from)
                        let proj = Path.relative(workspace.rootPath, proj)
                        Fs.appendFileSync( file', "") |> unbox
                        Forge.addFileBelow from proj file'
                )
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.addFile", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (_, proj, name, _,_,_,_,_) ->
                let opts = createEmpty<InputBoxOptions>
                opts.placeHolder <- Some "new.fs"
                opts.prompt <- Some "New file name, relative to opened directory"
                opts.value <- Some "new.fs"
                window.showInputBox(opts)
                |> Promise.map (fun file ->
                    if JS.isDefined file then
                        let file' = Path.join(Path.dirname proj, file)
                        let proj = Path.relative(workspace.rootPath, proj)
                        Fs.appendFileSync( file', "") |> unbox
                        Forge.addFile proj file'
                )
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add


        commands.registerCommand("fsharp.explorer.addProjecRef", Func<obj, obj>(fun m ->
            match unbox m with
            | ProjectReferencesList (_, _, p) ->
                Forge.addProjectReferencePath p
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.removeProjecRef", Func<obj, obj>(fun m ->
            match unbox m with
            | ProjectReference (_, path, _, p) ->
                Forge.removeProjectReferencePath path p
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add
        
        let treeOptions = createEmpty<CreateTreeViewOptions<Model>>
        treeOptions.treeDataProvider <- provider
        let treeView = window.createTreeView(treeViewId, treeOptions)
        context.subscriptions.Add treeView

        AutoReveal.activate context rootChanged.event treeView

        let wsProvider =
            let viewLoading path =
                "<b>Status:</b> loading.."
            let viewParsed (proj: Project) =
                [ yield "<b>Status:</b> parsed correctly"
                  yield ""
                  yield sprintf "<b>Project</b>: %s" proj.Project
                  yield ""
                  yield sprintf "<b>Output Type</b>: %s" proj.OutputType
                  yield sprintf "<b>Output</b>: %s" proj.Output
                  yield ""
                  match proj.Info with
                  | ProjectResponseInfo.DotnetSdk info ->
                      yield sprintf "<b>Project Type</b>: .NET Sdk (dotnet/sdk)"
                      yield ""
                      yield sprintf "<b>Configuration</b>: %s" info.Configuration
                      yield sprintf "<b>Target Framework</b>: %s (%s %s)" info.TargetFramework info.TargetFrameworkIdentifier info.TargetFrameworkVersion
                      yield ""
                      let boolToString x = if x then "yes" else "no"
                      yield sprintf "<b>Restored successfully</b>: %s" (info.RestoreSuccess |> boolToString)
                      yield ""
                      let crossgen = not (info.TargetFrameworks |> Seq.isEmpty)
                      yield sprintf "<b>Crossgen (multiple target frameworks)</b>: %s" (crossgen |> boolToString)
                      if crossgen then
                        yield "<b>NOTE atm the target framework choosen by the language service is the first one in the list (transitive targer framework of libs should be ok). Is not possibile "
                            + "yet to choose, but you can change the position it in the fsproj</b>"
                        yield "TODO add link to wiki/issue "
                      yield "<ul>"
                      for tfm in info.TargetFrameworks do
                        yield sprintf "<li>%s</li>" tfm
                      yield "</ul>"
                  | ProjectResponseInfo.Verbose ->
                      yield sprintf "<b>Project Type</b>: old/verbose sdk"
                  | ProjectResponseInfo.ProjectJson ->
                      yield sprintf "<b>Project Type</b>: project.json"
                  ]
                |> String.concat "<br />"
            let viewFailed path error =
                [ "<b>Status:</b> failed to load"; ""
                  "<b>Error:</b>"
                  error ]
                |> String.concat "<br />"

            { new TextDocumentContentProvider with
                  member this.provideTextDocumentContent (uri: Uri) =
                      match uri.path with
                      | "/projects/status" ->
                          let q = Querystring.parse(uri.query)
                          let path : string = q?path |> unbox
                          match Project.tryFindInWorkspace path with
                          | None ->
                              sprintf "Project '%s' not found" path
                          | Some (Project.ProjectLoadingState.Loading path) ->
                              viewLoading path
                          | Some (Project.ProjectLoadingState.Loaded proj) ->
                              viewParsed proj
                          | Some (Project.ProjectLoadingState.NotRestored (path,error)) ->
                              viewFailed path error
                          | Some (Project.ProjectLoadingState.Failed (path, error)) ->
                              viewFailed path error
                      | _ ->
                          sprintf "Requested uri: %s" (uri.toString())
            }

        vscode.workspace.registerTextDocumentContentProvider(DocumentSelector.Case1 "fsharp-workspace", wsProvider)
        |> context.subscriptions.Add

        let projectStatusUri projectPath = vscode.Uri.parse(sprintf "fsharp-workspace://authority/projects/status?path=%s" (JS.encodeURIComponent(projectPath)))

        let projectStatusCommand m =
            let showStatus path name =
                vscode.commands.executeCommand("vscode.previewHtml", projectStatusUri path, vscode.ViewColumn.One, (sprintf "Project %s status" name))
            match m with
            | ProjectFailedToLoad (_, path, name, _) ->
                showStatus path name
            | ProjectNotRestored (_, path, name, _) ->
                showStatus path name
            | Model.ProjectLoading (_, path, name) ->
                showStatus path name
            | Model.Project (_, path, name, _, _, _, _, _) ->
                showStatus path name
            | _ ->
                Promise.empty

        let runDebug m =
            match m with
            | Model.Project (_, path, name, _, _, _, _, proj) ->
                proj |> Debugger.buildAndDebug
            | _ ->
                Promise.empty

        let setLaunchSettingsCommand m =

            let findCoreclrLaunch debuggerRuntime cfg : LaunchJsonVersion2.RequestLaunch option =
                match unbox cfg?``type``, unbox cfg?request with
                | debuggerRuntime, "launch" -> Some (cfg |> unbox)
                | _ -> None

            match m with
            | Model.Project (_, path, name, _, _, _, _, proj) ->
                promise {
                    let launchConfig = workspace.getConfiguration("launch")
                    do! LaunchJsonVersion2.assertVersion2 launchConfig
                    let configurations : ResizeArray<obj> =
                        match launchConfig.get("configurations") with
                        | Some x -> x
                        | None -> ResizeArray<_>()

                    let launchRequestCfg =
                        let debuggerRuntime = Debugger.debuggerRuntime proj
                        // create or update right launch setting
                        match configurations :> seq<_> |> Seq.tryPick (findCoreclrLaunch debuggerRuntime) with
                        | Some cfg -> cfg
                        | None ->
                            //TODO is possibile to programmatically run the "Add Configuration" for .net console?
                            let cfg = LaunchJsonVersion2.createRequestLaunch ()
                            cfg?``type`` <- Debugger.debuggerRuntime proj
                            configurations.Add(cfg)
                            cfg

                    launchRequestCfg |> Debugger.setProgramPath proj

                    do! launchConfig.update("configurations", configurations, false)
                }
            | _ ->
                Promise.empty

        commands.registerCommand("fsharp.explorer.showProjectLoadFailedInfo", (unbox >> projectStatusCommand >> box))
        |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.showProjectStatus", (unbox >> projectStatusCommand >> box))
        |> context.subscriptions.Add

        // commands.registerCommand("fsharp.explorer.setLaunchSettings", (unbox >> setLaunchSettingsCommand >> box))
        // |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.project.debug", (unbox >> runDebug >> box))
        |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.openProjectFile", Func<obj, obj>(fun m ->
            let pathOpt =
                match unbox m with
                | ProjectNotLoaded (_, path, _) -> Some path
                | ProjectLoading (_, path, _) -> Some path
                | ProjectFailedToLoad (_, path, _, _) -> Some path
                | ProjectNotRestored (_, path, _, _) -> Some path
                | Project (_, path, _, _, _, _, _, _) -> Some path
                | _ -> None

            match pathOpt with
            | Some path ->
                commands.executeCommand("vscode.open", Uri.file(path))
                |> unbox
            | None -> undefined

        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.msbuild.build", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (_, path, _, _, _, _, _, pr) ->
                MSBuild.buildProjectPath "Build" pr
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.msbuild.rebuild", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (_, path, _, _, _, _, _, pr) ->
                MSBuild.buildProjectPath "Rebuild" pr
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.msbuild.clean", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (_, path, _, _, _, _, _, pr) ->
                MSBuild.buildProjectPath "Clean" pr
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.msbuild.restore", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (_, path, _, _, _, _, _, pr) ->
                MSBuild.restoreProjectPath pr
                |> unbox
            | ProjectNotRestored (_, path, _, _) ->
                MSBuild.restoreProjectWithoutParseData path
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.solution.build", Func<obj, obj>(fun m ->
            match unbox m with
            | Solution (_, path, _, _) ->
                MSBuild.buildSolution "Build" path
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.solution.rebuild", Func<obj, obj>(fun m ->
            match unbox m with
            | Solution (_, path, _, _) ->
                MSBuild.buildSolution "Rebuild" path
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.solution.clean", Func<obj, obj>(fun m ->
            match unbox m with
            | Solution (_, path, _, _) ->
                MSBuild.buildSolution "Clean" path
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.solution.restore", Func<obj, obj>(fun m ->
            match unbox m with
            | Solution (_, path, _, _) ->
                MSBuild.buildSolution "Restore" path
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.project.run", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (_, _, _, _, _, _, _, pr) ->
                Debugger.buildAndRun pr
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.project.setDefault", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (_, _, _, _, _, _, _, pr) ->
                Debugger.setDefaultProject pr
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.project.generateFSI", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (_, _, _, _, _, _, _, pr) ->
                Fsi.generateProjectReferencesForProject pr
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.project.sendFSI", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (_, _, _, _, _, _, _, pr) ->
                Fsi.sendReferencesForProject pr
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.solution.addProject", Func<obj, obj>(fun m ->
            match unbox m with
            | Solution (_, _,name, _) ->
                promise {
                    let projects = Project.getAll ()
                    // let addedProject = Project.getLoaded () |> List.map (fun p -> p.Project)
                    // let toAdd = projects |> List.where (fun n -> addedProject |> List.contains n |> not) |> ResizeArray
                    if projects.Length = 0 then
                        window.showInformationMessage "No projects in workspace that can be added to the solution" |> ignore
                    else
                        let projs = projects |> ResizeArray
                        let! proj = window.showQuickPick (unbox projs)
                        if JS.isDefined proj then
                            Project.execWithDotnet MSBuild.outputChannel (sprintf "sln %s add %s" name proj) |> ignore
                }
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        ()

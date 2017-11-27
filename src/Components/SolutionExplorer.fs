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
open Ionide.VSCode.Helpers

module SolutionExplorer =


    type Model =
        | Workspace of Projects : Model list
        | Solution of path: string * name: string * items: Model list
        | WorkspaceFolder of name: string * items: Model list
        | ReferenceList of References: Model list * projectPath : string
        | ProjectReferencesList of Projects : Model list * ProjectPath : string
        | ProjectNotLoaded of path: string * name: string
        | ProjectLoading of path: string * name: string
        | ProjectFailedToLoad of path: string * name: string * error: string
        | ProjectNotRestored of path: string * name: string * error: string
        | Project of path: string * name: string * Files: Model list * ProjectReferencesList : Model  * ReferenceList: Model * isExe : bool * project : DTO.Project
        | Folder of name : string * path: string * Files : Model list
        | File of path: string * name: string * projectPath : string
        | Reference of path: string * name: string * projectPath : string
        | ProjectReference of path: string * name: string * projectPath : string

    type NodeEntry = {
        Key : string
        Children : Dictionary<string, NodeEntry>
    }

    let mutable loadedTheme: VsCodeIconTheme.Loaded option = None

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

    let rec toModel folder pp (entry : NodeEntry)  =
        let f = (folder + Path.sep + entry.Key)
        if entry.Children.Count > 0 then
            let childs =
                entry.Children
                |> Seq.map (fun n -> toModel f pp n.Value )
                |> Seq.toList
            Folder(entry.Key, f, childs)
        else
            let p = (Path.dirname pp) + f
            File(p, entry.Key, pp)



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
            |> List.map (fun p -> Reference(p,Path.basename p, proj.Project))
            |> fun n -> ReferenceList(n, proj.Project)

        let projs =
            proj.References
            |> List.choose (fun r ->
                projects
                |> Array.tryFind (fun pr -> pr.Output = r))
            |> List.map (fun p -> ProjectReference(p.Project, Path.basename(p.Project, ".fsproj"), proj.Project))
            |> fun n -> ProjectReferencesList(n, proj.Project)

        let name = Path.basename(proj.Project, ".fsproj")
        Project(proj.Project, name,files, projs, refs, Project.isExeProject proj, proj)

    let private getProjectModelByState proj =
        match proj with
        | Project.ProjectLoadingState.Loading p ->
            Model.ProjectLoading (p, Path.basename p)
        | Project.ProjectLoadingState.Loaded proj ->
            getProjectModel proj
        | Project.ProjectLoadingState.Failed (p, err) ->
            Model.ProjectFailedToLoad (p, Path.basename p, err)
        | Project.ProjectLoadingState.NotRestored (p, err) ->
            Model.ProjectNotRestored (p, Path.basename p, err)

    let getFolders model =
        let rec loop model lst =
            match model with
            | Workspace fls -> fls |> List.collect (fun x -> loop x lst )
            | Project (_,_,fls,_, _, _, _) -> fls |> List.collect (fun x -> loop x lst )
            | Folder (_, f, fls) ->
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
                Model.ProjectNotLoaded (projPath, (Path.basename projPath))
            | Some p ->
                getProjectModelByState p
        let rec getItem (item: WorkspacePeekFoundSolutionItem) =
            match item.Kind with
            | WorkspacePeekFoundSolutionItemKind.Folder folder ->
                let files =
                    folder.Files
                    |> Array.map (fun f -> Model.File (f,Path.basename(f),""))
                let items = folder.Items |> Array.map getItem
                Model.WorkspaceFolder (item.Name, (Seq.append files items |> List.ofSeq))
            | MsbuildFormat proj ->
                getProjItem item.Name
        match ws with
        | WorkspacePeekFound.Solution sln ->
            let s = Solution (sln.Path, (Path.basename sln.Path), (sln.Items |> Array.map getItem |> List.ofArray))
            Workspace [s]
        | WorkspacePeekFound.Directory dir ->
            Workspace (dir.Fsprojs |> Array.map getProjItem |> List.ofArray)

    let private getSolution () =
        Project.getLoadedSolution ()
        |> Option.map getSolutionModel

    let private getSubmodel node =
        match node with
        | Workspace projects -> projects
        | WorkspaceFolder (_, items) -> items
        | Solution (_,_, items) -> items
        | ProjectNotLoaded _ -> []
        | ProjectLoading _ -> []
        | ProjectFailedToLoad _ -> []
        | ProjectNotRestored _ -> []
        | Project (_, _, files, projs, refs, _, _) ->
            [
                 // SHOULD REFS BE DISPLAYED AT ALL? THOSE ARE RESOLVED BY MSBUILD REFS
                yield refs
                yield projs
                yield! files
            ]
        | ReferenceList (refs, _) -> refs
        | ProjectReferencesList (refs, _) -> refs
        | Folder (_,_,files) -> files
        | File _ -> []
        | Reference _ -> []
        | ProjectReference _ -> []

    let private getLabel node =
        match node with
        | Workspace _ -> "Workspace"
        | WorkspaceFolder (name,_) -> name
        | Solution (_, name, _) -> name
        | ProjectNotLoaded (_, name) -> sprintf "%s (not loaded yet)" name
        | ProjectLoading (_, name) -> sprintf "%s (loading..)" name
        | ProjectFailedToLoad (_, name, _) -> sprintf "%s (load failed)" name
        | ProjectNotRestored (_, name, _) -> sprintf "%s (not restored)" name
        | Project (_, name,_, _,_, _, _) -> name
        | ReferenceList _ -> "References"
        | ProjectReferencesList (refs, _) -> "Project References"
        | Folder (n,_, _) -> n
        | File (_, name, _) -> name
        | Reference (_, name, _) ->
            if name.ToLowerInvariant().EndsWith(".dll") then
                name.Substring(0, name.Length - 4)
            else
                name
        | ProjectReference (_, name, _) -> name

    let private getRoot () =
        defaultArg (getSolution ()) (Workspace [])

    let private createProvider (emiter : EventEmitter<Model>) : TreeDataProvider<Model> =
        let plugPath =
            try
                (VSCode.getPluginPath "Ionide.ionide-fsharp")
            with
            | _ ->  (VSCode.getPluginPath "Ionide.Ionide-fsharp")

        { new TreeDataProvider<Model>
          with
            member this.onDidChangeTreeData =
                emiter.event

            member this.getChildren(node) =
                if JS.isDefined node then
                    getSubmodel node |> ResizeArray
                else
                    getRoot () |> getSubmodel |> ResizeArray

            member this.getTreeItem(node) =
                let ti = createEmpty<TreeItem>

                ti.label <- getLabel node

                let collaps =
                    match node with
                    | File _ | Reference _ | ProjectReference _ ->
                        vscode.TreeItemCollapsibleState.None
                    | ProjectFailedToLoad _ | ProjectLoading _ | ProjectNotLoaded _ | ProjectNotRestored _
                    | Workspace _ | Solution _ ->
                        vscode.TreeItemCollapsibleState.Expanded
                    | WorkspaceFolder (_, items) ->
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

                ti.collapsibleState <- Some collaps

                let command =
                    match node with
                    | File (p, _, _)  ->
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
                    | Project (_, _, _, _, _, false, _)  -> "project"
                    | Project (_, _, _, _, _, true, _)  -> "projectExe"
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

                let iconFromTheme (f: VsCodeIconTheme.Loaded -> VsCodeIconTheme.ResolvedIcon) light dark =
                    let fromTheme = loadedTheme |> Option.map f
                    p.light <- defaultArg (fromTheme |> Option.bind (fun x -> x.light)) (plugPath + light)
                    p.dark <- defaultArg (fromTheme |> Option.bind (fun x -> x.dark)) (plugPath + dark)
                    Some p

                let icon =
                    match node with
                    | File (path, _, _) ->
                        let fileName = Path.basename(path)
                        iconFromTheme (VsCodeIconTheme.getFileIcon fileName None false) "/images/file-code-light.svg" "/images/file-code-dark.svg"
                    | Project (path, _, _, _, _, _, _) | Solution (path, _, _)  ->
                        let fileName = Path.basename(path)
                        iconFromTheme (VsCodeIconTheme.getFileIcon fileName None false) "/images/project-light.svg" "/images/project-dark.svg"
                    | Folder (name,_, _) | WorkspaceFolder (name, _)  ->
                        iconFromTheme (VsCodeIconTheme.getFolderIcon name) "/images/folder-light.svg" "/images/folder-dark.svg"
                    | Reference _ | ProjectReference _ ->
                        p.light <- plugPath + "/images/circuit-board-light.svg"
                        p.dark <- plugPath + "/images/circuit-board-dark.svg"
                        Some p
                    | _ -> None
                ti.iconPath <- icon

                ti
        }

    let loadCurrentTheme (reloadTree: EventEmitter<Model>) = promise {
        let configured = VsCodeIconTheme.getConfigured() |> Option.bind VsCodeIconTheme.getInfo
        match configured with
        | Some configured ->
            if loadedTheme.IsNone || loadedTheme.Value.info.id <> configured.id then
                let! loaded = VsCodeIconTheme.load configured
                loadedTheme <- loaded
                reloadTree.fire (undefined)
        | None ->
            if loadedTheme.IsSome then
                loadedTheme <- None
                reloadTree.fire (undefined)
    }

    let activate (context: ExtensionContext) =
        let emiter = EventEmitter<Model>()

        let provider = createProvider emiter

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
            | File (p, _, _) ->
                Forge.moveFileUpPath p
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.moveDown", Func<obj, obj>(fun m ->
            match unbox m with
            | File (p, _, _) ->
                Forge.moveFileDownPath p
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.moveToFolder", Func<obj, obj>(fun m ->
            let folders =
                getRoot()
                |> getFolders

            match unbox m with
            | File (p, _, pp) ->
                Forge.moveFileToFolder folders p pp
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.removeFile", Func<obj, obj>(fun m ->
            match unbox m with
            | File (p, _, _) ->
                Forge.removeFilePath p
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.renameFile", Func<obj, obj>(fun m ->
            match unbox m with
            | File (old, _, proj) ->
                Forge.renameFilePath old proj
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.addAbove", Func<obj, obj>(fun m ->
            match unbox m with
            | File (from, name, proj) ->
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
            | File (from, name, proj) ->
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
            | Project (proj, name, _,_,_,_,_) ->
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
            | ProjectReferencesList (_, p) ->
                Forge.addProjectReferencePath p
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.removeProjecRef", Func<obj, obj>(fun m ->
            match unbox m with
            | ProjectReference (path, _, p) ->
                Forge.removeProjectReferencePath path p
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        window.registerTreeDataProvider("ionide.projectExplorer", provider )
        |> context.subscriptions.Add

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
            | ProjectFailedToLoad (path, name, _) ->
                showStatus path name
            | ProjectNotRestored (path, name, _) ->
                showStatus path name
            | Model.ProjectLoading (path, name) ->
                showStatus path name
            | Model.Project (path, name, _, _, _, _, _) ->
                showStatus path name
            | _ ->
                Promise.empty

        let runDebug m =
            match m with
            | Model.Project (path, name, _, _, _, _, proj) ->
                proj |> Debugger.buildAndDebug
            | _ ->
                Promise.empty

        let setLaunchSettingsCommand m =

            let findCoreclrLaunch debuggerRuntime cfg : LaunchJsonVersion2.RequestLaunch option =
                match unbox cfg?``type``, unbox cfg?request with
                | debuggerRuntime, "launch" -> Some (cfg |> unbox)
                | _ -> None

            match m with
            | Model.Project (path, name, _, _, _, _, proj) ->
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
                | ProjectNotLoaded (path, _) -> Some path
                | ProjectLoading (path, _) -> Some path
                | ProjectFailedToLoad (path, _, _) -> Some path
                | ProjectNotRestored (path, _, _) -> Some path
                | Project (path, _, _, _, _, _, _) -> Some path
                | _ -> None

            match pathOpt with
            | Some path ->
                commands.executeCommand("vscode.open", Uri.file(path))
                |> unbox
            | None -> undefined

        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.msbuild.build", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (path, _, _, _, _, _, pr) ->
                MSBuild.buildProjectPath "Build" pr
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.msbuild.rebuild", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (path, _, _, _, _, _, pr) ->
                MSBuild.buildProjectPath "Rebuild" pr
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.msbuild.clean", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (path, _, _, _, _, _, pr) ->
                MSBuild.buildProjectPath "Clean" pr
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.msbuild.restore", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (path, _, _, _, _, _, pr) ->
                MSBuild.restoreProjectPath pr
                |> unbox
            | ProjectNotRestored (path, _, _) ->
                MSBuild.restoreProjectWithoutParseData path
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.solution.build", Func<obj, obj>(fun m ->
            match unbox m with
            | Solution (path, _, _) ->
                MSBuild.buildSolution "Build" path
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.solution.rebuild", Func<obj, obj>(fun m ->
            match unbox m with
            | Solution (path, _, _) ->
                MSBuild.buildSolution "Rebuild" path
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.solution.clean", Func<obj, obj>(fun m ->
            match unbox m with
            | Solution (path, _, _) ->
                MSBuild.buildSolution "Clean" path
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.solution.restore", Func<obj, obj>(fun m ->
            match unbox m with
            | Solution (path, _, _) ->
                MSBuild.buildSolution "Restore" path
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.project.run", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (_, _, _, _, _, _, pr) ->
                Debugger.buildAndRun pr
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.project.setDefault", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (_, _, _, _, _, _, pr) ->
                Debugger.setDefaultProject pr
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.project.generateFSI", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (_, _, _, _, _, _, pr) ->
                Fsi.generateProjectReferencesForProject pr
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.project.sendFSI", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (_, _, _, _, _, _, pr) ->
                Fsi.sendReferencesForProject pr
                |> unbox
            | _ -> undefined
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.solution.addProject", Func<obj, obj>(fun m ->
            match unbox m with
            | Solution (_,name, _) ->
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

        workspace.onDidChangeConfiguration.Invoke(fun _ ->
            loadCurrentTheme emiter |> ignore
            null) |> ignore
        loadCurrentTheme emiter |> ignore
        ()

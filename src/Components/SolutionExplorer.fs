namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open Ionide.VSCode.Helpers
open System.Text.RegularExpressions

open DTO

module node = Node.Api

module SolutionExplorer =

    type Model =
        | Workspace of Projects: Model list
        | Solution of parent: Model option ref * path: string * name: string * items: Model list
        | WorkspaceFolder of parent: Model option ref * name: string * items: Model list
        | PackageReferenceList of parent: Model option ref * References: Model list * projectPath: string
        | ProjectReferencesList of parent: Model option ref * Projects: Model list * ProjectPath: string
        | ProjectNotLoaded of parent: Model option ref * path: string * name: string
        | ProjectLoading of parent: Model option ref * path: string * name: string
        | ProjectFailedToLoad of parent: Model option ref * path: string * name: string * error: string
        | ProjectNotRestored of parent: Model option ref * path: string * name: string * error: string
        | ProjectLanguageNotSupported of parent: Model option ref * path: string * name: string
        | Project of
            parent: Model option ref *
            path: string *
            name: string *
            Files: Model list *
            ProjectReferencesList: Model *
            ReferenceList: Model *
            isExe: bool *
            project: DTO.Project
        | Folder of parent: Model option ref * name: string * path: string * Files: Model list * projectPath: string
        | File of
            parent: Model option ref *
            path: string *
            name: string *
            virtualPath: string option *
            projectPath: string
        | PackageReference of parent: Model option ref * path: string * name: string * projectPath: string
        | ProjectReference of parent: Model option ref * path: string * name: string * projectPath: string

    type NodeEntry =
        { Key: string
          FilePath: string
          VirtualPath: string
          mutable Children: NodeEntry list }

    let inline pathCombine a b = a + node.path.sep + b

    let add' root (virtualPath: string) filepath =
        let rec addhelper state items =
            match items, state.Children with
            | [], _ -> state
            | [ key ], children when children |> List.exists (fun c -> c.Key = key) -> state
            | [ key ], _ ->
                let x =
                    { Key = Uri.UnescapeDataString key
                      FilePath = filepath
                      VirtualPath = virtualPath
                      Children = [] }

                state.Children <- x :: state.Children
                state
            | dirName :: xs, lastFileOrDir :: _ when dirName = lastFileOrDir.Key -> addhelper lastFileOrDir xs
            | dirName :: xs, _ ->
                let dirPath = pathCombine state.FilePath dirName

                let x =
                    { Key = Uri.UnescapeDataString dirName
                      FilePath = dirPath
                      VirtualPath = virtualPath
                      Children = [] }

                state.Children <- x :: state.Children
                addhelper x xs

        virtualPath.Split('/') |> List.ofArray |> addhelper root |> ignore


    let getParentRef (model: Model) =
        match model with
        | Workspace _ -> ref None
        | Solution (parent, _, _, _) -> parent
        | WorkspaceFolder (parent, _, _) -> parent
        | PackageReferenceList (parent, _, _) -> parent
        | ProjectReferencesList (parent, _, _) -> parent
        | ProjectNotLoaded (parent, _, _) -> parent
        | ProjectLoading (parent, _, _) -> parent
        | ProjectFailedToLoad (parent, _, _, _) -> parent
        | ProjectNotRestored (parent, _, _, _) -> parent
        | ProjectLanguageNotSupported (parent, _, _) -> parent
        | Project (parent, _, _, _, _, _, _, _) -> parent
        | Folder (parent, _, _, _, _) -> parent
        | File (parent, _, _, _, _) -> parent
        | PackageReference (parent, _, _, _) -> parent
        | ProjectReference (parent, _, _, _) -> parent

    let inline setParentRef (model: Model) (parent: Model) =
        let parentRef = getParentRef model
        parentRef.Value <- Some parent

    let setParentRefs (models: #seq<Model>) (parent: Model) =
        for model in models do
            setParentRef model parent

    let dirName = node.path.dirname

    (*
    let dirName = System.IO.Path.GetDirectoryName

    let pathCombine = System.IO.Path.Combine

    let projPath = @"d:\my\my.fsproj"

    let projItems =
        [("My/AssemblyInfo.fs", "c:\prova.fs"); ("a.fs", "e:\aa.fa");
           ("My/b.fs", "e:\bb.fs")]
*)

    let rec toModel (projPath: string) (entry: NodeEntry) =
        if entry.Children.Length > 0 then
            let childs = entry.Children |> Seq.map (toModel projPath) |> Seq.toList

            let result = Folder(ref None, entry.Key, entry.FilePath, childs, projPath)
            setParentRefs childs result
            result
        else
            File(ref None, entry.FilePath, entry.Key, Some entry.VirtualPath, projPath)

    let buildTree projPath (files: (string * string) list) =
        let projDir = dirName projPath

        let entry =
            { Key = ""
              FilePath = projDir
              VirtualPath = ""
              Children = [] }

        files |> List.iter (fun (virtualPath, path) -> add' entry virtualPath path)

        entry.Children |> Seq.rev |> Seq.map (toModel projPath) |> Seq.toList

    let private getProjectModel (proj: Project) =
        let projects = Project.getLoaded () |> Seq.toArray

        let files =
            proj.Items
            |> Seq.filter (fun p -> p.Name = "Compile")
            |> Seq.map (fun p -> p.VirtualPath, p.FilePath)
            |> Seq.toList
            |> buildTree proj.Project

        let packageRefs =
            proj.PackageReferences
            |> Seq.distinctBy (fun p -> p.Name)
            |> Seq.map (fun p -> PackageReference(ref None, p.FullPath, p.Name + " " + p.Version, proj.Project))
            |> Seq.toList
            |> fun n ->
                let result = PackageReferenceList(ref None, n, proj.Project)
                setParentRefs n result
                result

        let projs =
            proj.ProjectReferences
            |> Seq.distinctBy (fun p -> p.ProjectFileName)
            |> Seq.map (fun p ->
                ProjectReference(
                    ref None,
                    p.ProjectFileName,
                    node.path.basename (p.ProjectFileName, ".fsproj"),
                    proj.Project
                ))
            |> Seq.toList
            |> fun n ->
                let result = ProjectReferencesList(ref None, n, proj.Project)
                setParentRefs n result
                result

        let name = node.path.basename (proj.Project, ".fsproj")

        let result =
            Project(ref None, proj.Project, name, files, projs, packageRefs, Project.isExeProject proj, proj)

        setParentRefs files result
        setParentRef packageRefs result
        setParentRef projs result
        result

    let private getProjectModelByState proj =
        match proj with
        | Project.ProjectLoadingState.Loading p -> Model.ProjectLoading(ref None, p, node.path.basename p)
        | Project.ProjectLoadingState.Loaded proj -> getProjectModel proj
        | Project.ProjectLoadingState.Failed (p, err) ->
            Model.ProjectFailedToLoad(ref None, p, node.path.basename p, err)
        | Project.ProjectLoadingState.NotRestored (p, err) ->
            Model.ProjectNotRestored(ref None, p, node.path.basename p, err)
        | Project.ProjectLoadingState.LanguageNotSupported (p) ->
            Model.ProjectLanguageNotSupported(ref None, p, node.path.basename p)


    let getFolders model =
        let rec loop model lst =
            match model with
            | Workspace fls -> fls |> List.collect (fun x -> loop x lst)
            | Project (_, _, _, fls, _, _, _, _) -> fls |> List.collect (fun x -> loop x lst)
            | Folder (_, _, f, fls, _) -> fls |> List.collect (fun x -> loop x lst @ [ f ])
            | _ -> []

        let lst =
            loop model []
            |> List.distinct
            |> List.map (fun n -> n.TrimStart('\\'))
            |> List.sort

        "." :: lst

    let private getSolutionModel (ws: WorkspacePeekFound) : Model =
        let getProjItem projPath =
            match Project.tryFindInWorkspace projPath with
            | None -> Model.ProjectNotLoaded(ref None, projPath, (node.path.basename projPath))
            | Some p -> getProjectModelByState p

        let rec getItem (item: WorkspacePeekFoundSolutionItem) =
            match item.Kind with
            | WorkspacePeekFoundSolutionItemKind.Folder folder ->
                let files =
                    folder.Files
                    |> Array.map (fun f -> Model.File(ref None, f, node.path.basename (f), None, ""))

                let items = folder.Items |> Array.map getItem

                let result =
                    Model.WorkspaceFolder(ref None, item.Name, (Seq.append files items |> List.ofSeq))

                setParentRefs files result
                setParentRefs items result
                result
            | MsbuildFormat _proj -> getProjItem item.Name

        match ws with
        | WorkspacePeekFound.Solution sln ->
            let s =
                Solution(
                    ref None,
                    sln.Path,
                    (node.path.basename sln.Path),
                    (sln.Items |> Array.map getItem |> List.ofArray)
                )

            let result = Workspace [ s ]
            setParentRef s result
            result
        | WorkspacePeekFound.Directory dir ->
            let items = dir.Fsprojs |> Array.map getProjItem |> List.ofArray

            let result = Workspace items
            setParentRefs items result
            result

    let private getSolution () =
        Project.getLoadedSolution () |> Option.map getSolutionModel

    let private getSubmodel node =
        match node with
        | Workspace projects -> projects
        | WorkspaceFolder (_, _, items) -> items
        | Solution (_, _, _, items) -> items
        | ProjectNotLoaded _ -> []
        | ProjectLoading _ -> []
        | ProjectFailedToLoad _ -> []
        | ProjectNotRestored _ -> []
        | ProjectLanguageNotSupported _ -> []
        | Project (_, _, _, files, projs, refs, _, _) ->
            [
              // SHOULD REFS BE DISPLAYED AT ALL? THOSE ARE RESOLVED BY MSBUILD REFS
              yield refs
              yield projs
              yield! files ]
        | PackageReferenceList (_, refs, _) -> refs
        | ProjectReferencesList (_, refs, _) -> refs
        | Folder (_, _, _, files, _) -> files |> List.rev
        | File _ -> []
        | PackageReference _ -> []
        | ProjectReference _ -> []

    let private getLabel node =
        match node with
        | Workspace _ -> "Workspace"
        | WorkspaceFolder (_, name, _) -> name
        | Solution (_, _, name, _) -> name
        | ProjectNotLoaded (_, _, name) -> sprintf "%s (not loaded yet)" name
        | ProjectLoading (_, _, name) -> sprintf "%s (loading..)" name
        | ProjectFailedToLoad (_, _, name, _) -> sprintf "%s (load failed)" name
        | ProjectNotRestored (_, _, name, _) -> sprintf "%s (not restored)" name
        | ProjectLanguageNotSupported (_, _, name) -> sprintf "%s (language not supported)" name
        | Project (_, _, name, _, _, _, _, _) -> name
        | PackageReferenceList _ -> "Package References"
        | ProjectReferencesList (_, refs, _) -> "Project References"
        | Folder (_, n, _, _, _) -> n
        | File (_, _, name, _, _) -> name
        | PackageReference (_, _, name, _) ->
            if name.ToLowerInvariant().EndsWith(".dll") then
                name.Substring(0, name.Length - 4)
            else
                name
        | ProjectReference (_, _, name, _) -> name

    let private getRoot () =
        defaultArg (getSolution ()) (Workspace [])

    let private createProvider
        (event: Event<U3<Model, ResizeArray<Model>, unit> option> option)
        (rootChanged: EventEmitter<Model>)
        : TreeDataProvider<Model> =
        let plugPath = VSCodeExtension.ionidePluginPath ()
        let mutable event = event

        { new TreeDataProvider<Model> with
            override this.getChildren(element: Model option) : ProviderResult<ResizeArray<Model>> =
                match element with
                | Some node ->
                    let r = getSubmodel node |> ResizeArray
                    Some(U2.Case1 r)
                | None ->
                    let root = getRoot ()
                    rootChanged.fire root
                    let r = root |> getSubmodel |> ResizeArray
                    Some(U2.Case1 r)

            override this.getParent(element: Model) : ProviderResult<Model> =
                let parentRef = getParentRef element

                match parentRef.Value with
                | None -> None
                | Some parentRef -> U2.Case1 parentRef |> Some

            override this.getTreeItem(element: Model) : U2<TreeItem, Thenable<TreeItem>> =
                let collaps: TreeItemCollapsibleState =
                    match element with
                    | File _
                    | PackageReference _
                    | ProjectReference _ -> TreeItemCollapsibleState.None
                    | ProjectFailedToLoad _
                    | ProjectLoading _
                    | ProjectNotLoaded _
                    | ProjectNotRestored _
                    | ProjectLanguageNotSupported _
                    | Workspace _
                    | Solution _ -> TreeItemCollapsibleState.Expanded
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
                            TreeItemCollapsibleState.Expanded
                        else
                            TreeItemCollapsibleState.Collapsed
                    | Folder _
                    | Project _
                    | ProjectReferencesList _
                    | PackageReferenceList _ -> TreeItemCollapsibleState.Collapsed

                let ti = vscode.TreeItem.Create(U2.Case1(getLabel element), collaps)

                let command =
                    match element with
                    | File (_, p, _, _, _) ->
                        let c = createEmpty<Command>
                        c.command <- "vscode.open"
                        c.title <- "open"
                        let options = createEmpty<obj>
                        options?preserveFocus <- true

                        c.arguments <- Some(ResizeArray [| Some(box (vscode.Uri.file p)); Some options |])

                        Some c
                    | _ -> None

                ti.command <- command

                let context =
                    match element with
                    | File _ -> "file"
                    | ProjectReferencesList _ -> "projectRefList"
                    | PackageReferenceList _ -> "referencesList"
                    | Project (_, _, _, _, _, _, false, _) -> "project"
                    | Project (_, _, _, _, _, _, true, _) -> "projectExe"
                    | ProjectReference _ -> "projectRef"
                    | PackageReference _ -> "reference"
                    | Folder _ -> "folder"
                    | ProjectFailedToLoad _ -> "projectLoadFailed"
                    | ProjectLoading _ -> "projectLoading"
                    | ProjectNotLoaded _ -> "projectNotLoaded"
                    | ProjectNotRestored _ -> "projectNotRestored"
                    | ProjectLanguageNotSupported _ -> "projectLanguageNotSupported"
                    | Solution _ -> "solution"
                    | Workspace _ -> "workspace"
                    | WorkspaceFolder _ -> "workspaceFolder"

                ti.contextValue <- Some(sprintf "ionide.projectExplorer.%s" context)

                let icon, resourceUri =
                    match element with
                    | File (_, path, _, _, _)
                    | ProjectNotLoaded (_, path, _)
                    | ProjectLoading (_, path, _)
                    | ProjectFailedToLoad (_, path, _, _)
                    | ProjectNotRestored (_, path, _, _)
                    | ProjectLanguageNotSupported (_, path, _) ->
                        vscode.ThemeIcon.File |> U4.Case4 |> Some, vscode.Uri.file path |> Some
                    | Project (_, path, _, _, _, _, _, _)
                    | Solution (_, path, _, _)
                    | Folder (_, _, path, _, _) ->
                        vscode.ThemeIcon.Folder |> U4.Case4 |> Some, vscode.Uri.file path |> Some
                    | PackageReferenceList _
                    | ProjectReferencesList _
                    | WorkspaceFolder _ -> vscode.ThemeIcon.Folder |> U4.Case4 |> Some, None
                    | PackageReference (_, path, _, _)
                    | ProjectReference (_, path, _, _) ->
                        let light = (plugPath + "/images/circuit-board-light.svg") |> U2.Case1

                        let dark = (plugPath + "/images/circuit-board-dark.svg") |> U2.Case1

                        let p = {| light = light; dark = dark |} |> U4.Case3
                        p |> Some, vscode.Uri.file path |> Some
                    | Workspace _ -> None, None

                ti.iconPath <- icon
                ti.resourceUri <- resourceUri

                ti.id <-
                    let label (ti: TreeItem) =
                        match ti.label with
                        | Some (U2.Case1 l) -> l
                        | Some (U2.Case2 l) -> l.label
                        | None -> ""

                    match element with
                    | PackageReferenceList (_, _, pp)
                    | ProjectReferencesList (_, _, pp)
                    | PackageReference (_, _, _, pp)
                    | ProjectReference (_, _, _, pp) -> Some(label ti + "||" + pp)
                    | Folder _ -> None
                    | File (_, _, _, _, pp) ->
                        (resourceUri
                         |> Option.map (fun u -> (label ti + "||" + u.toString () + "||" + pp)))
                    | _ -> (resourceUri |> Option.map (fun u -> (label ti + "||" + u.toString ())))

                U2.Case1 ti

            member this.onDidChangeTreeData: Event<_> option = event

            member this.onDidChangeTreeData
                with set (v: Event<_> option): unit = event <- v

            override this.resolveTreeItem
                (
                    item: TreeItem,
                    element: Model,
                    token: CancellationToken
                ) : ProviderResult<TreeItem> =
                Some(U2.Case1 item) }

    module private ShowInActivity =

        let private setInFsharpActivity =
            Context.cachedSetter<bool> "fsharp.showProjectExplorerInFsharpActivity"

        let private setInExplorerActivity =
            Context.cachedSetter<bool> "fsharp.showProjectExplorerInExplorerActivity"

        let initializeAndGetId () : string =
            let showIn = "FSharp.showProjectExplorerIn" |> Configuration.get "fsharp"

            let inFsharpActivity = (showIn = "fsharp")
            setInFsharpActivity inFsharpActivity
            setInExplorerActivity (not inFsharpActivity)

            if inFsharpActivity then
                "ionide.projectExplorerInActivity"
            else
                "ionide.projectExplorer"

    module NodeReveal =
        module private RevealConfiguration =
            [<Literal>]
            let private autoKey = "FSharp.autoRevealInExplorer"

            [<Literal>]
            let private autoCodeExplorerKey = "explorer.autoReveal"

            let getAutoReveal () =
                match Configuration.get "sameAsFileExplorer" autoKey with
                | "enabled" -> true
                | "disabled" -> false
                | _ -> Configuration.get true autoCodeExplorerKey

        type private State =
            { RootModel: Model
              ModelPerFile: Map<string, Model> }

        let private findModelFromUri (state: State option ref) (uri: Uri) =
            if uri.scheme = "file" && JS.isDefined uri.fsPath then
                state.Value |> Option.bind (fun s -> s.ModelPerFile |> Map.tryFind uri.fsPath)
            else
                None

        let private revealUri (tree: TreeView<Model>) (state: State option ref) (uri: Uri) (showTreeIfHidden: bool) =
            if showTreeIfHidden || tree.visible then
                let model = findModelFromUri state uri

                match model with
                | Some model ->
                    let options =
                        {| select = Some true
                           expand = Some(U2.Case1 false)
                           focus = None |}

                    tree.reveal (model, options) |> ignore
                | _ -> ()

        let private revealTextEditor
            (tree: TreeView<Model>)
            (state: State option ref)
            (textEditor: TextEditor option)
            (showTreeIfHidden: bool)
            =
            match textEditor with
            | Some textEditor -> revealUri tree state textEditor.document.uri showTreeIfHidden
            | None -> ()

        let private onDidChangeActiveTextEditor
            (tree: TreeView<Model>)
            (state: State option ref)
            (textEditor: TextEditor option)
            =
            if RevealConfiguration.getAutoReveal () then
                revealTextEditor tree state textEditor false

        let rec private getModelPerFile (model: Model) : (string * Model) list =
            match model with
            | File (_, path, _, _, _)
            | ProjectNotLoaded (_, path, _)
            | ProjectLoading (_, path, _)
            | ProjectFailedToLoad (_, path, _, _)
            | ProjectNotRestored (_, path, _, _)
            | ProjectLanguageNotSupported (_, path, _) -> [ path, model ]
            | Project (_, path, _, children, _, _, _, _)
            | Solution (_, path, _, children) ->
                let current = path, model
                let forChildren = children |> List.collect getModelPerFile
                current :: forChildren
            | Folder (_, _, _, children, _)
            | WorkspaceFolder (_, _, children)
            | Workspace children -> children |> List.collect getModelPerFile
            | PackageReference _
            | ProjectReference _
            | PackageReferenceList _
            | ProjectReferencesList _ -> []

        let private onModelChanged (tree: TreeView<Model>) (state: State option ref) (newValue: Model) =
            let modelPerFile = getModelPerFile newValue |> Map.ofList

            let newState =
                Some
                    { RootModel = newValue
                      ModelPerFile = modelPerFile }

            state.Value <- newState

            if RevealConfiguration.getAutoReveal () then
                revealTextEditor tree state window.activeTextEditor false

        let private onDidChangeTreeVisibility
            (tree: TreeView<Model>)
            (state: State option ref)
            (change: TreeViewVisibilityChangeEvent)
            =
            if change.visible && RevealConfiguration.getAutoReveal () then
                // Done out of the event call to avoid VSCode double-selecting due to a race-condition
                JS.setTimeout (fun () -> revealTextEditor tree state window.activeTextEditor true) 0
                |> ignore

        let activate (context: ExtensionContext) (rootChanged: Event<Model>) (treeView: TreeView<Model>) =
            let state: State option ref = ref None

            let onDidChangeActiveTextEditor' = onDidChangeActiveTextEditor treeView state

            window.onDidChangeActiveTextEditor.Invoke(unbox onDidChangeActiveTextEditor')
            |> context.Subscribe

            let onModelChanged' = onModelChanged treeView state

            rootChanged.Invoke(unbox onModelChanged') |> context.Subscribe

            let onDidChangeTreeVisibility' = onDidChangeTreeVisibility treeView state

            treeView.onDidChangeVisibility.Invoke(unbox onDidChangeTreeVisibility')
            |> context.Subscribe

            commands.registerCommand (
                "fsharp.revealInSolutionExplorer",
                (fun _ -> revealTextEditor treeView state window.activeTextEditor true)
                |> objfy2
            )
            |> context.Subscribe


    let private handleUntitled (fn: string) =
        if fn.EndsWith ".fs" || fn.EndsWith ".fsi" || fn.EndsWith ".fsx" then
            fn
        else
            (fn + ".fs")

    let newProject () =
        promise {
            let! templates = LanguageService.dotnetNewList ()

            let n =
                templates
                |> List.map (fun t ->
                    let res = createEmpty<QuickPickItem>
                    res.label <- t.Name
                    res.description <- Some t.ShortName
                    res)
                |> ResizeArray

            let cwd = workspace.rootPath

            match cwd with
            | Some cwd ->
                let! template = window.showQuickPick (n |> U2.Case1)

                match template with
                | Some template ->
                    let opts = createEmpty<InputBoxOptions>
                    opts.prompt <- Some "Project directory, relative to workspace root (-o parameter)"
                    let! dir = window.showInputBox (opts)

                    let opts = createEmpty<InputBoxOptions>
                    opts.prompt <- Some "Project name (-n parameter)"
                    let! name = window.showInputBox (opts)

                    match dir, name with
                    | Some dir, Some name ->
                        let output = if String.IsNullOrWhiteSpace dir then None else Some dir

                        let projName = if String.IsNullOrWhiteSpace name then None else Some name

                        match template.description with
                        | None -> return ()
                        | Some description ->
                            let! _ = LanguageService.dotnetNewRun description projName output
                            let model = getSolution ()
                            //If it's the solution workspace we want to add project to the solution
                            match model with
                            | Some (Workspace [ Solution (_, _, slnName, _) ]) ->
                                let pname =
                                    if projName.IsSome then
                                        projName.Value + ".fsproj"
                                    else if output.IsSome then
                                        output.Value + ".fsproj"
                                    else
                                        (node.path.dirname workspace.rootPath.Value) + ".fsproj"

                                let proj = node.path.join (workspace.rootPath.Value, dir, name, pname)
                                let args = [ "sln"; slnName; "add"; proj ]

                                Project.execWithDotnet MSBuild.outputChannel (ResizeArray args) |> ignore
                            | _ ->
                                //If it's the first project in the workspace we need to init the workspace
                                if Project.getInWorkspace().IsEmpty then
                                    do! Project.initWorkspace ()

                        ()
                    | _ -> ()
                | None -> ()
            | None -> window.showErrorMessage ("No open folder.") |> ignore
        }

    let activate (context: ExtensionContext) =
        let emiter = vscode.EventEmitter.Create<_>()
        let rootChanged = vscode.EventEmitter.Create<Model>()

        let provider = createProvider (Some(emiter.event)) rootChanged

        let treeViewId = ShowInActivity.initializeAndGetId ()

        Project.workspaceChanged.Invoke(fun _ ->
            emiter.fire None
            None)
        |> context.Subscribe

        commands.registerCommand ("fsharp.NewProject", newProject |> objfy2)
        |> context.Subscribe


        commands.registerCommand (
            "fsharp.explorer.refresh",
            objfy2 (fun _ ->
                emiter.fire None
                None)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.clearCache",
            objfy2 (fun _ ->
                Project.clearCache ()
                None)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.moveUp",
            objfy2 (fun m ->
                match unbox m with
                | File (_, _, name, Some virtPath, proj) -> FsProjEdit.moveFileUpPath proj virtPath
                | _ -> undefined
                |> ignore

                None)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.moveDown",
            objfy2 (fun m ->
                match unbox m with
                | File (_, _, name, Some virtPath, proj) -> FsProjEdit.moveFileDownPath proj virtPath
                | _ -> undefined
                |> ignore

                None)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.removeFile",
            objfy2 (fun m ->
                match unbox m with
                | File (_, _, name, Some virtPath, proj) -> FsProjEdit.removeFilePath proj virtPath
                | _ -> undefined
                |> ignore

                None)
        )
        |> context.Subscribe


        commands.registerCommand (
            "fsharp.explorer.addAbove",
            objfy2 (fun m ->
                match unbox m with
                | File (_, _, name, Some virtPath, proj) ->
                    let opts = createEmpty<InputBoxOptions>
                    opts.placeHolder <- Some "new.fs"
                    opts.prompt <- Some "New file name, relative to selected file"
                    opts.value <- Some "new.fs"

                    window.showInputBox (opts)
                    |> Promise.ofThenable
                    |> Promise.bind (fun file ->
                        match file with
                        | Some file ->
                            let file' = handleUntitled file
                            FsProjEdit.addFileAbove proj virtPath file'
                        | None -> Promise.empty)
                    |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.addBelow",
            objfy2 (fun m ->
                match unbox m with
                | File (_, fr_om, name, Some virtPath, proj) ->
                    let opts = createEmpty<InputBoxOptions>
                    opts.placeHolder <- Some "new.fs"
                    opts.prompt <- Some "New file name, relative to selected file"
                    opts.value <- Some "new.fs"

                    window.showInputBox (opts)
                    |> Promise.ofThenable
                    |> Promise.map (fun file ->
                        match file with
                        | Some file ->
                            let file' = handleUntitled file
                            FsProjEdit.addFileBelow proj virtPath file'
                        | None -> Promise.empty)
                    |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.addFile",
            objfy2 (fun m ->
                match unbox m with
                | Project (_, proj, _, _, _, _, _, _) ->
                    let opts = createEmpty<InputBoxOptions>
                    opts.placeHolder <- Some "new.fs"
                    opts.prompt <- Some "New file name, relative to project file"
                    opts.value <- Some "new.fs"

                    window.showInputBox (opts)
                    |> Promise.ofThenable
                    |> Promise.map (fun file ->
                        match file with
                        | Some file ->
                            let file' = handleUntitled file
                            FsProjEdit.addFile proj file'
                        | None -> Promise.empty)
                    |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.addProjecRef",
            objfy2 (fun m ->
                match unbox m with
                | ProjectReferencesList (_, _, p) -> FsProjEdit.addProjectReferencePath (Some p)
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.removeProjecRef",
            objfy2 (fun m ->
                match unbox m with
                | ProjectReference (_, path, _, p) -> FsProjEdit.removeProjectReferencePath path p
                | _ -> undefined)
        )
        |> context.Subscribe

        let treeOptions = createEmpty<TreeViewOptions<Model>>
        treeOptions.treeDataProvider <- provider
        let treeView = window.createTreeView (treeViewId, treeOptions)
        context.subscriptions.Add(unbox (box treeView))

        NodeReveal.activate context rootChanged.event treeView

        let wsProvider =
            let viewLoading path = "<b>Status:</b> loading.."

            let viewLanguageNotSupported path = "<b>Status:</b> language not supported"

            let viewParsed (proj: Project) =
                match getProjectModel proj with
                | (Project (_,
                            _,
                            _,
                            files,
                            ProjectReferencesList (_, projRefs, _),
                            PackageReferenceList (_, refs, _),
                            _,
                            project)) ->
                    let files = project.Files

                    let projRefs = project.ProjectReferences |> Array.map (fun n -> n.ProjectFileName)

                    let packageRefs =
                        project.PackageReferences |> Array.map (fun n -> n.Name + " " + n.Version)

                    let refs =
                        refs
                        |> List.filter (function
                            | PackageReference _ -> true
                            | _ -> false)
                        |> List.map (function
                            | PackageReference (_, p, _, _) -> p
                            | _ -> failwith "Should not happend, we filtered the `refs` list before")

                    let info = proj.Info

                    [ yield "<b>Status:</b> parsed correctly"
                      yield ""
                      yield sprintf "<b>Project</b>: %s" proj.Project
                      yield ""
                      yield sprintf "<b>Output Type</b>: %s" proj.OutputType
                      yield sprintf "<b>Output</b>: %s" proj.Output

                      yield ""

                      yield sprintf "<b>Project Type</b>: .NET Sdk (dotnet/sdk)"
                      yield ""
                      yield sprintf "<b>Configuration</b>: %s" info.Configuration
                      yield
                          sprintf
                              "<b>Target Framework</b>: %s (%s %s)"
                              info.TargetFramework
                              info.TargetFrameworkIdentifier
                              info.TargetFrameworkVersion
                      yield ""
                      let boolToString x = if x then "yes" else "no"
                      yield sprintf "<b>Restored successfully</b>: %s" (info.RestoreSuccess |> boolToString)
                      yield ""
                      let crossgen = not (info.TargetFrameworks |> Seq.isEmpty)
                      yield sprintf "<b>Crossgen (multiple target frameworks)</b>: %s" (crossgen |> boolToString)

                      if crossgen then
                          yield
                              "<b>NOTE: You're using multiple target frameworks. As of now you can't choose which target framework should be used by FSAC. Instead, the first target framework from the list is selected. To change the target framework used by FSAC, simply place it on the first position on the &lt;TargetFrameworks&gt; list.</b>"

                          yield
                              "For more info see this issue: https://github.com/ionide/ionide-vscode-fsharp/issues/278"

                          yield "<ul>"

                          for tfm in info.TargetFrameworks do
                              yield sprintf "<li>%s</li>" tfm

                          yield "</ul>"

                      yield ""
                      yield "<b>Files</b>:"
                      yield! files
                      yield ""
                      yield "<b>Project References</b>:"
                      yield! projRefs
                      yield ""
                      yield "<b>Package References</b>:"
                      yield! packageRefs
                      yield ""
                      yield "<b>Resolved References</b>:"
                      yield! refs ]
                    |> String.concat "<br />"
                | _ -> "Failed to generate status report..."

            let viewFailed path error =
                let sdkErrorRegex =
                    Regex(
                        "A compatible SDK version for global\.json version: \[([\d.]+)\].*was not found.*",
                        RegexOptions.IgnoreCase
                    )

                let errorMsg =
                    match sdkErrorRegex.Match error with
                    | m when m.Success ->
                        let version = m.Groups.[1].Value

                        [ sprintf "A compatible SDK version for global.json version: <b>%s</b> was not found." version
                          ""
                          "If you haven't installed a compatible version on your computer, you can go to: <a href=\"https://dotnet.microsoft.com/download/archives\">https://dotnet.microsoft.com/download/archives</a> to download it."
                          ""
                          "<hr/>"
                          "<b>Original error:</b>"
                          ""
                          error ]
                    | _ -> [ error ]

                [ "<b>Status:</b> failed to load"; ""; "<b>Error:</b>" ] @ errorMsg
                |> String.concat "<br />"

            let mutable e = None

            { new TextDocumentContentProvider with
                member this.onDidChange: Event<Uri> option = e

                member this.onDidChange
                    with set (v: Event<Uri> option): unit = e <- v

                member this.provideTextDocumentContent(uri: Uri, token: CancellationToken) : ProviderResult<string> =
                    let message =
                        match uri.path with
                        | "projects/status" ->
                            let q = node.querystring.parse (uri.query)
                            let path: string = q?path |> unbox

                            match Project.tryFindInWorkspace path with
                            | None -> sprintf "Project '%s' not found" path
                            | Some (Project.ProjectLoadingState.Loading path) -> viewLoading path
                            | Some (Project.ProjectLoadingState.Loaded proj) -> viewParsed proj
                            | Some (Project.ProjectLoadingState.NotRestored (path, error)) -> viewFailed path error
                            | Some (Project.ProjectLoadingState.Failed (path, error)) -> viewFailed path error
                            | Some (Project.ProjectLoadingState.LanguageNotSupported path) ->
                                viewLanguageNotSupported path
                        | _ -> sprintf "Requested uri: %s" (uri.toString ())

                    U2.Case1 message |> Some }

        workspace.registerTextDocumentContentProvider ("fsharp-workspace", wsProvider)
        |> context.Subscribe

        let getStatusText (path: string) =
            promise {
                // let! res = vscode.window.showInputBox()
                let url = sprintf "fsharp-workspace:projects/status?path=%s" path
                let uri = vscode.Uri.parse (url)
                let! doc = workspace.openTextDocument (uri)
                return doc.getText ()
            }

        let projectStatusCommand m =
            match m with
            | ProjectFailedToLoad (_, path, name, _) -> ShowStatus.CreateOrShow(path, name)
            | ProjectNotRestored (_, path, name, _) -> ShowStatus.CreateOrShow(path, name)
            | Model.ProjectLoading (_, path, name) -> ShowStatus.CreateOrShow(path, name)
            | Model.Project (_, path, name, _, _, _, _, proj) -> ShowStatus.CreateOrShow(path, name)
            | _ -> ()

        let runDebug m =
            match m with
            | Model.Project (_, path, name, _, _, _, _, proj) -> proj |> Debugger.buildAndDebug
            | _ -> Promise.empty

        let setLaunchSettingsCommand m =
            let findCoreclrLaunch debuggerRuntime cfg : LaunchJsonVersion2.RequestLaunch option =
                match unbox cfg?``type``, unbox cfg?request with
                | debuggerRuntime, "launch" -> Some(cfg |> unbox)
                | _ -> None

            match m with
            | Model.Project (_, path, name, _, _, _, _, proj) ->
                promise {
                    let launchConfig = workspace.getConfiguration ("launch")
                    do! LaunchJsonVersion2.assertVersion2 launchConfig

                    let configurations: ResizeArray<obj> =
                        match launchConfig.get ("configurations") with
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

                    do!
                        launchConfig.update (
                            "configurations",
                            Some(box configurations),
                            configurationTarget = U2.Case2 false
                        )
                }
            | _ -> Promise.empty

        commands.registerCommand ("fsharp.explorer.showProjectLoadFailedInfo", (objfy2 projectStatusCommand))
        |> context.Subscribe

        commands.registerCommand ("fsharp.explorer.showProjectStatus", (objfy2 projectStatusCommand))
        |> context.Subscribe

        commands.registerCommand ("fsharp.explorer.project.debug", (objfy2 runDebug))
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.openProjectFile",
            objfy2 (fun m ->
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
                    commands.executeCommand ("vscode.open", Some(box (vscode.Uri.file (path))))
                    |> unbox
                | None -> undefined

            )
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.msbuild.build",
            objfy2 (fun m ->
                match unbox m with
                | Project (_, path, _, _, _, _, _, pr) -> MSBuild.buildProjectPath "Build" pr |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.msbuild.rebuild",
            objfy2 (fun m ->
                match unbox m with
                | Project (_, path, _, _, _, _, _, pr) -> MSBuild.buildProjectPath "Rebuild" pr |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.msbuild.clean",
            objfy2 (fun m ->
                match unbox m with
                | Project (_, path, _, _, _, _, _, pr) -> MSBuild.buildProjectPath "Clean" pr |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.msbuild.restore",
            objfy2 (fun m ->
                match unbox m with
                | Project (_, path, _, _, _, _, _, pr) -> MSBuild.restoreKnownProject pr |> unbox
                | ProjectNotRestored (_, path, _, _) -> MSBuild.restoreProjectAsync path |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.solution.build",
            objfy2 (fun m ->
                match unbox m with
                | Solution (_, path, _, _) -> MSBuild.buildSolution "Build" path |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.solution.rebuild",
            objfy2 (fun m ->
                match unbox m with
                | Solution (_, path, _, _) -> MSBuild.buildSolution "Rebuild" path |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.solution.clean",
            objfy2 (fun m ->
                match unbox m with
                | Solution (_, path, _, _) -> MSBuild.buildSolution "Clean" path |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.solution.restore",
            objfy2 (fun m ->
                match unbox m with
                | Solution (_, path, _, _) -> MSBuild.buildSolution "Restore" path |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.project.run",
            objfy2 (fun m ->
                match unbox m with
                | Project (_, _, _, _, _, _, _, pr) -> Debugger.buildAndRun pr |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.project.setDefault",
            objfy2 (fun m ->
                match unbox m with
                | Project (_, _, _, _, _, _, _, pr) -> Debugger.setDefaultProject pr |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.project.generateFSI",
            objfy2 (fun m ->
                match unbox m with
                | Project (_, _, _, _, _, _, _, pr) -> Fsi.generateProjectReferencesForProject pr |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.project.sendFSI",
            objfy2 (fun m ->
                match unbox m with
                | Project (_, _, _, _, _, _, _, pr) -> Fsi.sendReferencesForProject pr |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fsharp.explorer.solution.addProject",
            objfy2 (fun m ->
                match unbox m with
                | Solution (_, _, name, _) ->
                    promise {
                        let projects = Project.getAll ()

                        if projects.Length = 0 then
                            window.showInformationMessage (
                                "No projects in workspace that can be added to the solution",
                                null
                            )
                            |> ignore
                        else
                            let projs = projects |> ResizeArray
                            let! proj = window.showQuickPick (unbox projs)

                            match proj with
                            | Some proj ->
                                let args = [ "sln"; name; "add"; proj ]

                                Project.execWithDotnet MSBuild.outputChannel (ResizeArray args) |> ignore
                            | None -> ()
                    }
                    |> unbox
                | _ -> undefined)
        )
        |> context.Subscribe

        ()

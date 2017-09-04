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
        | ReferenceList of References: Model list * projectPath : string
        | ProjectReferencesList of Projects : Model list * ProjectPath : string
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
        let sep = "\\"

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
        let f = (folder + path.sep + entry.Key)
        if entry.Children.Count > 0 then
            let childs =
                entry.Children
                |> Seq.map (fun n -> toModel f pp n.Value )
                |> Seq.toList
            Folder(entry.Key, f, childs)
        else
            let p = (path.dirname pp) + f
            File(p, entry.Key, pp)



    let buildTree pp (files : string list) =
        let entry = {Key = ""; Children = new Dictionary<_,_>()}
        files |> List.iter (fun x -> add' entry x 0 |> ignore )
        entry.Children
        |> Seq.map (fun n -> toModel "" pp n.Value )
        |> Seq.toList



    let getProjectModel proj =
        let projects = Project.getLoaded ()

        let files =
            proj.Files
            |> List.map (fun p -> path.relative(path.dirname proj.Project, p))
            |> buildTree proj.Project

        let refs = proj.References |> List.map (fun p -> Reference(p, path.basename p, proj.Project)) |> fun n -> ReferenceList(n, proj.Project)
        let projs = proj.References |> List.choose (fun r -> projects |> Seq.tryFind (fun pr -> pr.Output = r)) |> List.map (fun p -> ProjectReference(p.Project, path.basename(p.Project, ".fsproj"), proj.Project)) |> fun n -> ProjectReferencesList(n, proj.Project)
        let name = path.basename(proj.Project, ".fsproj")
        Project(proj.Project, name,files, projs, refs, Project.isExeProject proj, proj)

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

    let private getModel() =
        let projects = Project.getLoaded ()
        projects
        |> Seq.toList
        |> List.map getProjectModel
        |> Workspace

    let rec private getSubmodel node =
        match node with
            | Workspace projects -> projects
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
        |> List.toArray

    let private getLabel node =
        match node with
        | Workspace _ -> "Workspace"
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
                    getModel () |> getSubmodel |> ResizeArray

            member this.getTreeItem(node) =
                let ti = createEmpty<TreeItem>
                ti.label <- getLabel node
                let collaps =
                    match node with
                    | File _ | Reference _ | ProjectReference _ -> None
                    | Workspace _ | Project _ -> Some TreeItemCollapsibleState.Expanded
                    | _ ->  Some TreeItemCollapsibleState.Collapsed
                ti.collapsibleState <- collaps
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
                    | File _  -> Some "ionide.projectExplorer.file"
                    | ProjectReferencesList _  -> Some "ionide.projectExplorer.projectRefList"
                    | ReferenceList _  -> Some "ionide.projectExplorer.referencesList"
                    | Project (_, _, _, _, _, false, _)  -> Some "ionide.projectExplorer.project"
                    | Project (_, _, _, _, _, true, _)  -> Some "ionide.projectExplorer.projectExe"
                    | ProjectReference _  -> Some "ionide.projectExplorer.projRef"
                    | Reference _  -> Some "ionide.projectExplorer.reference"
                    | _ -> None
                ti.contextValue <- context

                let p = createEmpty<TreeIconPath>

                let iconFromTheme (f: VsCodeIconTheme.Loaded -> VsCodeIconTheme.ResolvedIcon) light dark =
                    let fromTheme = loadedTheme |> Option.map f
                    p.light <- defaultArg (fromTheme |> Option.bind (fun x -> x.light)) (plugPath + light)
                    p.dark <- defaultArg (fromTheme |> Option.bind (fun x -> x.dark)) (plugPath + dark)
                    Some p

                let icon =
                    match node with
                    | File (path, _, _) ->
                        let fileName = Node.path.basename(path)
                        iconFromTheme (VsCodeIconTheme.getFileIcon fileName None false) "/images/file-code-light.svg" "/images/file-code-dark.svg"
                    | Project (path, _, _, _, _, _, _) ->
                        let fileName = Node.path.basename(path)
                        iconFromTheme (VsCodeIconTheme.getFileIcon fileName None false) "/images/project-light.svg" "/images/project-dark.svg"
                    | Folder (name,_, _)  ->
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
                reloadTree.fire (unbox ())
        | None ->
            if loadedTheme.IsSome then
                loadedTheme <- None
                reloadTree.fire (unbox ())
    }

    let activate (context: ExtensionContext) =
        let emiter = EventEmitter<Model>()
        let provider = createProvider emiter

        Project.projectChanged.event.Invoke(fun proj ->
            emiter.fire (unbox ()) |> unbox)
        |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.refresh", Func<obj, obj>(fun _ ->
            emiter.fire (unbox ()) |> unbox
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
            | _ -> unbox ()
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.moveDown", Func<obj, obj>(fun m ->
            match unbox m with
            | File (p, _, _) ->
                Forge.moveFileDownPath p
                |> unbox
            | _ -> unbox ()
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.moveToFolder", Func<obj, obj>(fun m ->
            let folders =
                getModel()
                |> getFolders

            match unbox m with
            | File (p, _, pp) ->
                Forge.moveFileToFolder folders p pp
                |> unbox
            | _ -> unbox ()
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.removeFile", Func<obj, obj>(fun m ->
            match unbox m with
            | File (p, _, _) ->
                Forge.removeFilePath p
                |> unbox
            | _ -> unbox ()
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.renameFile", Func<obj, obj>(fun m ->
            match unbox m with
            | File (old, _, proj) ->
                Forge.renameFilePath old proj
                |> unbox
            | _ -> unbox ()
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.addProjecRef", Func<obj, obj>(fun m ->
            match unbox m with
            | ProjectReferencesList (_, p) ->
                Forge.addProjectReferencePath p
                |> unbox
            | _ -> unbox ()
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.removeProjecRef", Func<obj, obj>(fun m ->
            match unbox m with
            | ProjectReference (path, _, p) ->
                Forge.removeProjectReferencePath path p
                |> unbox
            | _ -> unbox ()
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.openProjectFile", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (path, _, _, _, _, _, _) ->
                commands.executeCommand("vscode.open", Uri.file(path))
                |> unbox
            | _ -> unbox ()
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.msbuild.build", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (path, _, _, _, _, _, pr) ->
                MSBuild.buildProjectPath "Build" pr
                |> unbox
            | _ -> unbox ()
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.msbuild.rebuild", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (path, _, _, _, _, _, pr) ->
                MSBuild.buildProjectPath "Rebuild" pr
                |> unbox
            | _ -> unbox ()
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.msbuild.clean", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (path, _, _, _, _, _, pr) ->
                MSBuild.buildProjectPath "Clean" pr
                |> unbox
            | _ -> unbox ()
        )) |> context.subscriptions.Add

        commands.registerCommand("fsharp.explorer.project.run", Func<obj, obj>(fun m ->
            match unbox m with
            | Project (_, _, _, _, _, _, pr) ->
                Debugger.buildAndRun pr
                |> unbox
            | _ -> unbox ()
        )) |> context.subscriptions.Add

        // commands.registerCommand("fsharp.explorer.project.debug", Func<obj, obj>(fun m ->
        //     match unbox m with
        //     | Project (_, _, _, _, _, _, pr) ->
        //         Debugger.buildAndDebug pr
        //         |> unbox
        //     | _ -> unbox ()
        // )) |> context.subscriptions.Add

        window.registerTreeDataProvider("ionide.projectExplorer", provider )
        |> context.subscriptions.Add

        workspace.onDidChangeConfiguration.Invoke(fun _ ->
            loadCurrentTheme emiter |> ignore
            null) |> ignore
        loadCurrentTheme emiter |> ignore
        ()

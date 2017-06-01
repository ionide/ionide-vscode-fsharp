namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers

open DTO
open Ionide.VSCode.Helpers

module SolutionExplorer =

    type Model =
        | Workspace of Projects : Model list
        | ReferenceList of References: Model list
        | FileList of Files: Model list
        | ProjectReferencesList of Projects : Model list
        | Project of path: string * name: string * FileList: Model  * ProjectReferencesList : Model  * ReferenceList: Model
        | File of path: string * name: string * projectPath : string
        | Reference of path: string * name: string * projectPath : string
        | ProjectReference of path: string * name: string * projectPath : string

    let getProjectModel proj =
        let projects = Project.getLoaded ()

        let files = proj.Files |> List.map ( fun p -> File(p, path.basename p, proj.Project)) |> FileList
        let refs = proj.References |> List.map (fun p -> Reference(p, path.basename p, proj.Project)) |> ReferenceList
        let projs = proj.References |> List.choose (fun r -> projects |> Seq.tryFind (fun pr -> pr.Output = r)) |> List.map (fun p -> ProjectReference(p.Project, path.basename(p.Project, ".fsproj"), proj.Project))  |> ProjectReferencesList
        let name = path.basename(proj.Project, ".fsproj")
        Project(proj.Project, name,files, projs, refs)

    let private getModel() =
        let projects = Project.getLoaded ()
        projects
        |> Seq.toList
        |> List.map getProjectModel
        |> Workspace


    let private getSubmodel node =
        match node with
            | Workspace projects -> projects
            | Project (_, _, files, projs, refs) -> [yield refs; yield projs; yield files] // SHOLD REFS BE DISPLAYED AT ALL? THOSE ARE RESOLVED BY MSBUILD REFS
            | ReferenceList refs -> refs
            | ProjectReferencesList refs -> refs
            | FileList files -> files
            | File _ -> []
            | Reference _ -> []
            | ProjectReference _ -> []
        |> List.toArray

    let private getLabel node =
        match node with
        | Workspace _ -> "Workspace"
        | Project (_, name,_, _,_) -> name
        | ReferenceList _ -> "References"
        | ProjectReferencesList refs -> "Project References"
        | FileList _ -> "Files"
        | File (_, name, _) -> name
        | Reference (_, name, _) -> name
        | ProjectReference (_, name, _) -> name


    let private createProvider (emiter : EventEmitter<Model>) : TreeDataProvider<Model> =


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
                    | Workspace _ | FileList _ | Project _ -> Some 2
                    | _ ->  Some 1
                ti.collapsibleState <- collaps
                let command =
                    match node with
                    | File _  ->
                        let c = createEmpty<Command>
                        c.command <- "ionide.projectExplorer.open"
                        c.title <- "open"
                        Some c
                    | _ -> None
                ti.command <- command
                let context =
                    match node with
                    | File _  -> Some "ionide.projectExplorer.file"
                    | FileList _  -> Some "ionide.projectExplorer.fileList"
                    | ProjectReferencesList _  -> Some "ionide.projectExplorer.projectRefList"
                    | ReferenceList _  -> Some "ionide.projectExplorer.referencesList"
                    | Project _  -> Some "ionide.projectExplorer.project"
                    | ProjectReference _  -> Some "ionide.projectExplorer.projRef"
                    | Reference _  -> Some "ionide.projectExplorer.reference"
                    | _ -> None
                ti.contextValue <- context
                ti
        }

    let activate () =
        let emiter = EventEmitter<Model>()
        let provider = createProvider emiter

        Project.projectChanged.event.Invoke(fun proj ->
            emiter.fire (unbox ()) |> unbox)
        |> ignore

        commands.registerCommand("ionide.projectExplorer.open",  unbox<Func<obj,obj>> (fun n ->
            match n with
            | File (path, _, _) ->
                commands.executeCommand("vscode.open", Uri.file path)
                |> ignore
            | _ -> ())) |> ignore

        commands.registerCommand("ionide.projectExplorer.editProject",  unbox<Func<obj,obj>> (fun n ->
            match n with
            | Project (path, _, _,_,_) ->
                commands.executeCommand("vscode.open", Uri.file path)
                |> ignore
            | _ -> ())) |> ignore

        commands.registerCommand("ionide.projectExplorer.moveUp",  unbox<Func<obj,obj>> (fun n ->
            match n with
            | File (path, _, _) -> Forge.moveFileUpPath path
            | _ -> ())) |> ignore

        commands.registerCommand("ionide.projectExplorer.moveDown",  unbox<Func<obj,obj>> (fun n ->
            match n with
            | File (path, _, _) -> Forge.moveFileDownPath path
            | _ -> ())) |> ignore

        commands.registerCommand("ionide.projectExplorer.remove",  unbox<Func<obj,obj>> (fun n ->
            match n with
            | File (path, _, _) -> Forge.removeFilePath path
            | _ -> ())) |> ignore

        commands.registerCommand("ionide.projectExplorer.addProjectReference",  unbox<Func<obj,obj>> (fun n ->
            match n with
            | Project (path, _,_, _, _) -> Forge.addProjectReferencePath path  |> ignore
            | _ -> ())) |> ignore

        commands.registerCommand("ionide.projectExplorer.removeProjectReference",  unbox<Func<obj,obj>> (fun n ->
            match n with
            | ProjectReference (path, _, proj) -> Forge.removeProjectReferencePath path proj |> ignore
            | _ -> ())) |> ignore


        window.registerTreeDataProviderForView("ionide.projectExplorer", provider )
        |> ignore

        ()

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
        | Folder of name : string * Files : Model list
        | File of path: string * name: string * projectPath : string
        | Reference of path: string * name: string * projectPath : string
        | ProjectReference of path: string * name: string * projectPath : string

    let getProjectModel proj =
        let projects = Project.getLoaded ()

        let files =
            proj.Files
            |> List.map ( fun p ->
                let n = path.relative(path.dirname proj.Project, p)

                File(p, n, proj.Project))
            |> FileList
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
            | Folder (name,files) -> files
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
        | Folder (n, _) -> n
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
                    | FileList _  -> Some "ionide.projectExplorer.fileList"
                    | ProjectReferencesList _  -> Some "ionide.projectExplorer.projectRefList"
                    | ReferenceList _  -> Some "ionide.projectExplorer.referencesList"
                    | Project _  -> Some "ionide.projectExplorer.project"
                    | ProjectReference _  -> Some "ionide.projectExplorer.projRef"
                    | Reference _  -> Some "ionide.projectExplorer.reference"
                    | _ -> None
                ti.contextValue <- context
                let plugPath =
                    try
                        (VSCode.getPluginPath "Ionide.ionide-fsharp")
                    with
                    | _ ->  (VSCode.getPluginPath "Ionide.Ionide-fsharp")

                let p = createEmpty<TreeIconPath>
                let icon =
                    match node with
                    | File _ ->
                        p.light <- plugPath + "/images/file-code-light.svg"
                        p.dark <- plugPath + "/images/file-code-dark.svg"
                        Some p
                    | Project _ ->
                        p.light <- plugPath + "/images/project-light.svg"
                        p.dark <- plugPath + "/images/project-dark.svg"
                        Some p
                    | Reference _ | ProjectReference _ ->
                        p.light <- plugPath + "/images/circuit-board-light.svg"
                        p.dark <- plugPath + "/images/circuit-board-dark.svg"
                        Some p
                    | _ -> None
                ti.iconPath <- icon

                ti
        }

    let activate () =
        let emiter = EventEmitter<Model>()
        let provider = createProvider emiter

        Project.projectChanged.event.Invoke(fun proj ->
            emiter.fire (unbox ()) |> unbox)
        |> ignore

        commands.registerCommand("fsharp.explorer.moveUp", Func<obj, obj>(fun m ->
            match unbox m with
            | File (p, _, _) ->
                Forge.moveFileUpPath p
                |> unbox
            | _ -> unbox ()
        )) |> ignore

        commands.registerCommand("fsharp.explorer.moveDown", Func<obj, obj>(fun m ->
            match unbox m with
            | File (p, _, _) ->
                Forge.moveFileDownPath p
                |> unbox
            | _ -> unbox ()
        )) |> ignore

        commands.registerCommand("fsharp.explorer.removeFile", Func<obj, obj>(fun m ->
            match unbox m with
            | File (p, _, _) ->
                Forge.removeFilePath p
                |> unbox
            | _ -> unbox ()
        )) |> ignore

        window.registerTreeDataProvider("ionide.projectExplorer", provider )
        |> ignore

        ()

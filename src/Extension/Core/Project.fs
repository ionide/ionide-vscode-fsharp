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

[<ReflectedDefinition>]
module Project =
    let private emptyProjectsMap = Map<ProjectFilePath,Project> []
    let mutable private loadedProjects = emptyProjectsMap

    let find p =
        let rec findFsProj dir =
            if fs.lstatSync(dir).isDirectory() then
                let files = fs.readdirSync dir
                let projfile = files |> Seq.tryFind(fun s -> s.EndsWith(".fsproj") || s.EndsWith "project.json")
                match projfile with
                | None ->
                    let parent = if dir.LastIndexOf(path.sep) > 0 then dir.Substring(0, dir.LastIndexOf path.sep) else ""
                    if System.String.IsNullOrEmpty parent then None else findFsProj parent
                | Some p -> dir + path.sep + p |> Some
            else None

        p |> path.dirname |> findFsProj

    let findAll () =
        let rec findProjs dir =
            let files = fs.readdirSync dir
            files
            |> Seq.toList
            |> List.collect(fun s' ->
                try
                    let s = dir + path.sep + s'
                    if s' = ".git" || s' = "paket-files" then
                        []
                    elif fs.statSync(s).isDirectory () then
                        findProjs (s)
                    else
                       if s.EndsWith ".fsproj" then [ s ] else []
                with
                | _ -> []
            )

        match workspace.rootPath with
        | null -> []
        | rootPath -> findProjs rootPath

    let getAll () =
        let rec findProjs dir =
            let files = fs.readdirSync dir
            files
            |> Seq.toList
            |> List.collect(fun s' ->
                try
                    let s = dir + path.sep + s'
                    if s' = ".git" || s' = "paket-files" then []
                    elif fs.statSync(s).isDirectory () then findProjs (s)
                    elif s.EndsWith ".fsproj" || s.EndsWith ".csproj" || s.EndsWith ".vbproj" then [ s ]
                    else []
                with
                | _ -> []
            )

        match workspace.rootPath with
        | null -> []
        | rootPath -> rootPath |> findProjs

    let private clearLoadedProjects () =
        loadedProjects <- emptyProjectsMap

    let load (path:string) =
        LanguageService.project path
        |> Promise.onSuccess (fun (pr:ProjectResult) ->
            loadedProjects <- (pr.Data.Project.ToUpperInvariant (), pr.Data) |> loadedProjects.Add)

    let tryFindLoadedProject (path:string) =
         loadedProjects.TryFind (path.ToUpperInvariant ())

    let tryFindLoadedProjectByFile (filePath:string) =
        loadedProjects
        |> Seq.choose (fun kvp ->
            let len =
                kvp.Value.Files
                |> List.filter (fun f -> (f.ToUpperInvariant ()) = (filePath.ToUpperInvariant ()))
                |> List.length
            if len > 0 then Some kvp.Value else None )
        |> Seq.tryHead

    let getLoaded () = loadedProjects |> Seq.map (fun kv -> kv.Value)

    let activate = clearLoadedProjects >> findAll >> (Promise.executeForAll load)
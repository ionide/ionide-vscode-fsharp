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

        p
        |> path.dirname
        |> findFsProj

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
            | rootPath -> rootPath |> findProjs

    let getAll () =
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
                       if s.EndsWith ".fsproj" || s.EndsWith ".csproj" || s.EndsWith ".vbproj" then [ s ] else []
                with
                | _ -> []
            )

        match workspace.rootPath with
            | null -> []
            | rootPath -> rootPath |> findProjs


    let activate () =
        match findAll () with
        | [] -> Promise.lift (null |> unbox)
        | [x] -> LanguageService.project x
        | x::tail ->
            tail
            |> List.fold (fun acc e -> acc |> Promise.bind(fun _ -> LanguageService.project e) )
               (LanguageService.project x)

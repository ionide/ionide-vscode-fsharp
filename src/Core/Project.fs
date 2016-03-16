namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.languages
open FunScript.TypeScript.path
open FunScript.TypeScript.fs

open DTO 
open Ionide.VSCode.Helpers

[<ReflectedDefinition>]
module Project =
    let find p =
        let rec findFsProj dir =
            if Globals.lstatSync(dir).isDirectory() then
                let files = Globals.readdirSync dir
                let projfile = files |> Array.tryFind(fun s -> s.EndsWith(".fsproj"))
                match projfile with
                | None ->
                    let parent = if dir.LastIndexOf(Globals.sep) > 0 then dir.Substring(0, dir.LastIndexOf Globals.sep) else ""
                    if System.String.IsNullOrEmpty parent then None else findFsProj parent
                | Some p -> dir + Globals.sep + p |> Some
            else None

        p
        |> Globals.dirname
        |> findFsProj
        
    let findAll () = 
        let rec findProjs dir = 
            let files = Globals.readdirSync dir 
            files
            |> Array.toList
            |> List.collect(fun s ->
                try
                    let s = dir + Globals.sep + s
                    if Globals.statSync(s).isDirectory () then
                        findProjs (s) 
                    else    
                       if s.EndsWith ".fsproj" then [ s ] else [] 
                with
                | _ -> []            
            ) 
    
        workspace.Globals.rootPath |> findProjs
        
    let activate () =
        match findAll () with
        | [] -> ()
        | [x] -> LanguageService.project x |> ignore
        | x::tail ->
            tail 
            |> List.fold (fun acc e -> acc |> Promise.bind(fun _ -> LanguageService.project e) )
               (LanguageService.project x)
            |> ignore
    
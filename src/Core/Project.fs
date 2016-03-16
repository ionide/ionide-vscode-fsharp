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
            |> Array.collect(fun s ->
                try
                    if Globals.statSync(s).isDirectory () then
                        findProjs (dir + Globals.sep + s) 
                    else    
                       if s.EndsWith ".fsproj" then [| s |] else [||] 
                with
                | _ -> [||]            
            ) 
    
        workspace.Globals.rootPath |> findProjs
        
    let activate () =
        findAll ()
        |> Array.iter (LanguageService.project >> ignore)
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
    let setAnyProjectContext = Context.cachedSetter<bool> "fsharp.project.any"
    let projectChanged = EventEmitter<Project>()

    let excluded = "FSharp.excludeProjectDirectories" |> Configuration.get [| ".git"; "paket-files" |]

    let find p =
        let rec findFsProj dir =
            if fs.lstatSync(dir).isDirectory() then
                let files = fs.readdirSync dir
                let projfile = files |> Seq.tryFind(fun s -> s.EndsWith(".fsproj"))
                match projfile with
                | None ->
                    let projfile = files |> Seq.tryFind(fun s -> s.EndsWith "project.json")
                    match projfile with
                    | None ->
                        try
                            let parent = if dir.LastIndexOf(path.sep) > 0 then dir.Substring(0, dir.LastIndexOf path.sep) else ""
                            if System.String.IsNullOrEmpty parent then None else findFsProj parent
                        with
                        | _ -> None
                    | Some p -> dir + path.sep + p |> Some
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
                    if excluded |> Array.contains s' then
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
                    if excluded |> Array.contains s' then []
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
        setAnyProjectContext false

    let load (path:string) =
        let projEquals (p1 : Project) (p2 : Project) =
            p1.Project.ToUpperInvariant() = p2.Project.ToUpperInvariant() &&
            List.forall2 (=) p1.Files p2.Files &&
            List.forall2 (=) p1.References p2.References

        LanguageService.project path
        |> Promise.onSuccess (fun (pr:ProjectResult) ->
            if isNotNull pr then
                match loadedProjects.TryFind (pr.Data.Project.ToUpperInvariant ()) with
                | Some existing when not (projEquals existing pr.Data)  ->
                    projectChanged.fire pr.Data
                | None -> projectChanged.fire pr.Data
                | _ -> ()
                loadedProjects <- (pr.Data.Project.ToUpperInvariant (), pr.Data) |> loadedProjects.Add
                setAnyProjectContext true
                )

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

    let getCaches () =
        let rec findProjs dir =
            let files = fs.readdirSync dir
            files
            |> Seq.toList
            |> List.collect(fun s' ->
                try
                    let s = dir + path.sep + s'
                    if excluded |> Array.contains s' then
                        []
                    elif fs.statSync(s).isDirectory () then
                        findProjs (s)
                    else
                       if s.EndsWith "fsac.cache" then [ s ] else []
                with
                | _ -> []
            )

        match workspace.rootPath with
        | null -> []
        | rootPath -> findProjs rootPath


    let clearCache () =
        let cached = getCaches ()
        cached |> Seq.iter fs.unlinkSync
        window.showInformationMessage("Cache cleared")


    let isANetCoreAppProject (project:Project) =
        let projectContent = (fs.readFileSync project.Project).ToString()
        let netCoreTargets =
            [ "<TargetFramework>netcoreapp"
              "<Project Sdk=\"" ]

        let findInProject (toFind:string) =
            projectContent.IndexOf(toFind) >= 0

        netCoreTargets |> Seq.exists findInProject

    let isNetCoreApp (project:Project) =
        let projectContent = (fs.readFileSync project.Project).ToString()
        let core = "<TargetFramework>netcoreapp"
        projectContent.IndexOf(core) >= 0

    let isSDKProject (project:Project) =
        let projectContent = (fs.readFileSync project.Project).ToString()
        let sdk = "<Project Sdk=\""
        projectContent.IndexOf(sdk) >= 0

    let isSDKProjectPath (project:string) =
        let projectContent = (fs.readFileSync project).ToString()
        let sdk = "<Project Sdk=\""
        projectContent.IndexOf(sdk) >= 0

    let isPortablePdbProject (project:Project) =
        let projectContent = (fs.readFileSync project.Project).ToString()
        let portable = """<DebugType>portable</DebugType>"""
        projectContent.IndexOf(portable) >= 0

    let isExeProject (project:Project) =
        match project.Output, isANetCoreAppProject project with
        | _, true -> true
        | out, _ when out |> String.endWith ".exe" -> true
        | _ -> false

    let private execWithDotnet outputChannel cmd =
        promise {
            let! dotnet = Environment.dotnet
            return Process.spawnWithNotification dotnet "" cmd outputChannel
        }

    let private exec exe outputChannel cmd =
        promise {
            return Process.spawnWithNotification exe "mono" cmd outputChannel
        }

    let private execWithDotnetWithShell cmd =
        promise {
            let! dotnet = Environment.dotnet
            return Process.spawnWithShell (sprintf "\"%s\"" dotnet) "" cmd
        }

    let private execWithShell exe cmd =
        promise {
            return Process.spawnWithShell exe "mono" cmd
        }

    let buildWithMsbuild outputChannel (project:Project) =
        promise {
            let! msbuild = Environment.msbuild
            return! Process.spawnWithNotification msbuild "" project.Project outputChannel
            |> Process.toPromise
        }

    let buildWithDotnet outputChannel (project:Project) =
        promise {
            let! childProcess = execWithDotnet outputChannel ("build " + project.Project)
            return!
                childProcess
                |> Process.toPromise
        }

    let getLauncher outputChannel (project:Project) =
        let execDotnet = fun args ->
            let cmd = "run -p " + project.Project + if String.IsNullOrEmpty args then "" else " -- " + args
            execWithDotnet outputChannel cmd
        match project.Output, isANetCoreAppProject project with
        | _, true -> Some execDotnet
        | out, _ when out |> String.endWith ".exe" -> Some (fun args -> exec out outputChannel args)
        | _ -> None

    let getLauncherWithShell  (project:Project) =
        let execDotnet = fun args ->
            let cmd = "run -p " + project.Project + if String.IsNullOrEmpty args then "" else " -- " + args
            execWithDotnetWithShell cmd
        match project.Output, isANetCoreAppProject project with
        | _, true -> Some execDotnet
        | out, _ when out |> String.endWith ".exe" -> Some (fun args -> execWithShell out args)
        | _ -> None


    let activate =
        let w = workspace.createFileSystemWatcher("**/*.fsproj")
        commands.registerCommand("fsharp.clearCache", clearCache |> unbox<Func<obj,obj>> ) |> ignore

        w.onDidCreate.Invoke(fun n -> load n.fsPath |> unbox) |> ignore
        w.onDidChange.Invoke(fun n -> load n.fsPath |> unbox) |> ignore
        clearLoadedProjects >> findAll >> (Promise.executeForAll load)
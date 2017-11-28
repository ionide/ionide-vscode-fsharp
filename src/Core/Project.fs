namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers

open DTO

module Project =

    [<RequireQualifiedAccess>]
    type ProjectLoadingState =
        | Loading of path: string
        | Loaded of proj: Project
        | Failed of path: string * error: string
        | NotRestored of path: string * error: string

    [<RequireQualifiedAccess>]
    type FSharpWorkspaceMode =
        | Directory // prefer the directory
        | Sln // prefer sln, if any, otherwise directory
        | IonideSearch // old behaviour, like directory but search is ionide's side

    [<RequireQualifiedAccess>]
    type FSharpWorkspaceLoader =
        | Projects // send to FSAC multiple "project" command
        | WorkspaceLoad // send to FSAC the workspaceLoad and use notifications

    let private emptyProjectsMap : Map<ProjectFilePath,ProjectLoadingState> = Map.empty
    let mutable private loadedProjects = emptyProjectsMap
    let mutable private loadedWorkspace : WorkspacePeekFound option = None
    let mutable workspaceNotificationAvaiable = false
    let setAnyProjectContext = Context.cachedSetter<bool> "fsharp.project.any"
    let workspaceChanged = EventEmitter<WorkspacePeekFound>()
    let projectNotRestoredLoaded = EventEmitter<string>()
    let projectLoaded = EventEmitter<Project>()

    let excluded = "FSharp.excludeProjectDirectories" |> Configuration.get [| ".git"; "paket-files" |]
    let deepLevel = "FSharp.workspaceModePeekDeepLevel" |> Configuration.get 2 |> max 0

    let getInWorkspace () =
        loadedProjects |> Map.toList |> List.map snd
    let tryFindInWorkspace (path:string) =
        loadedProjects |> Map.tryFind (path.ToUpperInvariant ())
    let updateInWorkspace (path: string) state =
        loadedProjects <- loadedProjects |> Map.add (path.ToUpperInvariant ()) state

    let private guessFor p =
        let rec findFsProj dir =
            if Fs.lstatSync(U2.Case1 dir).isDirectory() then
                let files = Fs.readdirSync (U2.Case1 dir)
                let projfile = files |> Seq.tryFind(fun s -> s.EndsWith(".fsproj"))
                match projfile with
                | None ->
                    let projfile = files |> Seq.tryFind(fun s -> s.EndsWith "project.json")
                    match projfile with
                    | None ->
                        try
                            let parent = if dir.LastIndexOf(Path.sep) > 0 then dir.Substring(0, dir.LastIndexOf Path.sep) else ""
                            if System.String.IsNullOrEmpty parent then None else findFsProj parent
                        with
                        | _ -> None
                    | Some p -> dir + Path.sep + p |> Some
                | Some p -> dir + Path.sep + p |> Some
            else None

        p |> Path.dirname |> findFsProj

    let private findAll () =
        let rec findProjs dir =
            let files = Fs.readdirSync (U2.Case1 dir)
            files
            |> Seq.toList
            |> List.collect(fun s' ->
                try
                    let s = dir + Path.sep + s'
                    if excluded |> Array.contains s' then
                        []
                    elif Fs.statSync(U2.Case1 s).isDirectory () then
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
            let files = Fs.readdirSync (U2.Case1 dir)
            files
            |> Seq.toList
            |> List.collect(fun s' ->
                try
                    let s = dir + Path.sep + s'
                    if excluded |> Array.contains s' then []
                    elif Fs.statSync(U2.Case1 s).isDirectory () then findProjs (s)
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
        updateInWorkspace path (ProjectLoadingState.Loading path)

        let loaded (pr:ProjectResult) =
            if isNotNull pr then
                projectLoaded.fire (pr.Data)
                Some (pr.Data.Project, (ProjectLoadingState.Loaded pr.Data))
            else
                None
        let failed (b: obj) =
            let (msg: string), (err: ErrorData) = unbox b
            match err with
            | ErrorData.ProjectNotRestored d ->
                projectNotRestoredLoaded.fire path
                Some (path, ProjectLoadingState.NotRestored (path, msg) )
            | _ ->
                Some (path, (ProjectLoadingState.Failed (path, msg)))

        LanguageService.project path
        |> Promise.either (loaded >> Promise.lift) (failed >> Promise.lift)
        |> Promise.map (fun proj ->
            match proj with
            | Some (path, state) ->
                updateInWorkspace path state
                loadedWorkspace |> Option.iter (workspaceChanged.fire)
                setAnyProjectContext true
            | None ->
                () )

    let private chooseLoaded = function ProjectLoadingState.Loaded p -> Some p | _ -> None

    let getLoaded = getInWorkspace >> List.choose chooseLoaded
    let tryFindLoadedProject = tryFindInWorkspace >> Option.bind chooseLoaded

    let tryFindLoadedProjectByFile (filePath:string) =
        getLoaded ()
        |> List.tryPick (fun v ->
            let len =
                v.Files
                |> List.filter (fun f -> (f.ToUpperInvariant ()) = (filePath.ToUpperInvariant ()))
                |> List.length
            if len > 0 then Some v else None )

    let rec foldFsproj (item: WorkspacePeekFoundSolutionItem) =
        match item.Kind with
        | WorkspacePeekFoundSolutionItemKind.Folder folder ->
            folder.Items |> Array.collect foldFsproj
        | WorkspacePeekFoundSolutionItemKind.MsbuildFormat msbuild ->
            [| item.Name, msbuild |]

    let isLoadingWorkspaceComplete () =
        //Yes, can be better with a lock/flag/etc.
        //but the real cleanup will be moving all the loading in FSAC
        //ref https://github.com/fsharp/FsAutoComplete/issues/192
        let projs =
            match loadedWorkspace with
            | None -> Array.empty
            | Some (WorkspacePeekFound.Directory dir) ->
                dir.Fsprojs
            | Some (WorkspacePeekFound.Solution sln) ->
                sln.Items
                |> Array.collect foldFsproj
                |> Array.map fst

        let loadingInProgress p =
            match tryFindInWorkspace p with
            | None
            | Some (ProjectLoadingState.Loading _) ->
                true
            | Some (ProjectLoadingState.Loaded _)
            | Some (ProjectLoadingState.Failed _)
            | Some (ProjectLoadingState.NotRestored _) ->
                false

        projs
        |> Array.exists loadingInProgress

    let find fsFile =
        // First check in loaded projects
        match tryFindLoadedProjectByFile fsFile with
        | Some p -> Choice1Of3 p // this was easy
        | None ->
            if isLoadingWorkspaceComplete () then
                // 2 load in progress, dont try to parse
                Choice2Of3 ()
            else
                // 3 loading is finished, but file not found. try guess (old behaviour)
                Choice3Of3 (guessFor fsFile)

    let getLoadedSolution () = loadedWorkspace

    let getCaches () =

        let rec findProjs dir =
            let files = Fs.readdirSync (U2.Case1 dir)
            files
            |> Seq.toList
            |> List.collect(fun s' ->
                try
                    let s = dir + Path.sep + s'
                    if excluded |> Array.contains s' then
                        []
                    elif Fs.statSync(U2.Case1 s).isDirectory () then
                        findProjs (s)
                    else
                       if s.EndsWith "fsac.cache" then [ s ] else []
                with
                | _ -> []
            )

        match workspace.rootPath with
        | null -> []
        | rootPath -> findProjs rootPath

    let clearCacheIfOutdated () =
        let cached = getCaches ()
        cached |> Seq.iter (fun p ->
            let stat = Fs.statSync(U2.Case1 p)
            if stat.mtime <= DateTime(2017, 08, 27) then
                printfn "Cache outdated %s" p
                Fs.unlinkSync (U2.Case1 p)
        )
    let clearCache () =
        let cached = getCaches ()
        cached |> Seq.iter (U2.Case1 >> Fs.unlinkSync)
        window.showInformationMessage("Cache cleared")

    let countProjectsInSln (sln: WorkspacePeekFoundSolution) =
        sln.Items |> Array.map foldFsproj |> Array.sumBy Array.length

    let pickFSACWorkspace (ws: WorkspacePeekFound list) =
        let text (x: WorkspacePeekFound) =
            match x with
            | WorkspacePeekFound.Directory dir ->
                sprintf "[DIR] %s     (%i projects)" dir.Directory dir.Fsprojs.Length
            | WorkspacePeekFound.Solution sln ->
                let relativeSln = Path.relative (workspace.rootPath, sln.Path)
                sprintf "[SLN] %s     (%i projects)" relativeSln (countProjectsInSln sln)
        match ws |> List.map (fun x -> (text x), x) with
        | [] ->
            None |> Promise.lift
        | projects ->
            promise {
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Place"
                let chooseFrom = projects |> List.map fst |> ResizeArray
                let! chosen = window.showQuickPick(chooseFrom |> U2.Case1, opts)
                return if JS.isDefined chosen
                       then projects |> Map.ofList |> Map.tryFind chosen
                       else None
            }

    let isANetCoreAppProject (project:Project) =
        let projectContent = (Fs.readFileSync project.Project).toString()
        let netCoreTargets =
            [ "<TargetFramework>netcoreapp"
              "<Project Sdk=\"" ]

        let findInProject (toFind:string) =
            projectContent.IndexOf(toFind) >= 0

        netCoreTargets |> Seq.exists findInProject

    let isNetCoreApp (project:Project) =
        let projectContent = (Fs.readFileSync project.Project).toString()
        let core = "<TargetFramework>netcoreapp"
        projectContent.IndexOf(core) >= 0

    let isNetCoreApp2 (project:Project) =
        let projectContent = (Fs.readFileSync project.Project).toString()
        let core = "<TargetFramework>netcoreapp2"
        projectContent.IndexOf(core) >= 0

    let isSDKProject (project:Project) =
        match project.Info with
        | ProjectResponseInfo.DotnetSdk _ -> true
        |  _ -> false

    let isSDKProjectPath (project:string) =
        let projectContent = (Fs.readFileSync project).toString()
        let sdk = "<Project Sdk=\""
        projectContent.IndexOf(sdk) >= 0

    let isPortablePdbProject (project:Project) =
        let projectContent = (Fs.readFileSync project.Project).toString()
        let portable = """<DebugType>portable</DebugType>"""
        projectContent.IndexOf(portable) >= 0

    let isExeProject (project:Project) =
        match project.Output, isANetCoreAppProject project with
        | _, true -> true
        | out, _ when out |> String.endWith ".exe" -> true
        | _ -> false

    let execWithDotnet outputChannel cmd =
        promise {
            let! dotnet = Environment.dotnet
            return Process.spawnWithNotification dotnet "" cmd outputChannel
        }

    let exec exe outputChannel cmd =
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

    let private getWorkspaceForModeIonideSearch () =
        promise {
            let fsprojs = findAll ()
            let wdir =
                { WorkspacePeekFoundDirectory.Directory = vscode.workspace.rootPath
                  Fsprojs = fsprojs |> Array.ofList }
            return WorkspacePeekFound.Directory wdir
        }

    let private workspacePeek () = promise {
        let! ws = LanguageService.workspacePeek (vscode.workspace.rootPath) deepLevel (excluded |> List.ofArray)
        return
            ws.Found
            |> Array.sortBy (fun x ->
                match x with
                | WorkspacePeekFound.Solution sln -> countProjectsInSln sln
                | WorkspacePeekFound.Directory _ -> -1)
           |> Array.rev
           |> List.ofArray
        }

    let private getWorkspaceForMode mode =
        match mode with
        | FSharpWorkspaceMode.Sln ->
            // prefer the sln, load directly the first one, otherwise ask
            promise {
                let! ws = workspacePeek ()
                let slns =
                    ws
                    |> List.choose (fun x -> match x with WorkspacePeekFound.Solution _ -> Some x | _ -> None)

                let! choosen =
                    match slns with
                    | [] ->
                        ws
                        |> List.tryPick (fun x -> match x with WorkspacePeekFound.Directory _ -> Some x | _ -> None)
                        |> Promise.lift
                    | [ sln ] ->
                        Promise.lift (Some sln)
                    | _ ->
                        pickFSACWorkspace ws
                return choosen
            }
        | FSharpWorkspaceMode.Directory ->
            // prefer the directory, like old behaviour (none) but search is done fsac side
            promise {
                let! ws = workspacePeek ()
                return
                    ws
                    |> List.tryPick (fun x -> match x with WorkspacePeekFound.Directory _ -> Some x | _ -> None)
             }
        | FSharpWorkspaceMode.IonideSearch | _ ->
            // old behaviour, initialize all fsproj found (vscode side)
            getWorkspaceForModeIonideSearch ()
            |> Promise.map Some

    let getWorkspaceModeFromConfig () =
        match "FSharp.workspaceMode" |> Configuration.get "sln" with
        | "directory" -> FSharpWorkspaceMode.Directory
        | "sln" -> FSharpWorkspaceMode.Sln
        | "ionideSearch" | _ -> FSharpWorkspaceMode.IonideSearch

    let getWorkspaceLoaderFromConfig () =
        match "FSharp.workspaceLoader" |> Configuration.get "projects" with
        | "workspaceLoad" -> FSharpWorkspaceLoader.WorkspaceLoad
        | "projects" | _ -> FSharpWorkspaceLoader.Projects

    let handleProjectParsedNotification res =
        let projStatus =
            match res with
            | Choice1Of3 (pr: ProjectResult) ->
                projectLoaded.fire (pr.Data)
                Some (true, pr.Data.Project, (ProjectLoadingState.Loaded pr.Data))
            | Choice2Of3 (pr: ProjectLoadingResult) ->
                Some (false, pr.Data.Project, (ProjectLoadingState.Loading pr.Data.Project))
            | Choice3Of3 (msg, err) ->
                match err with
                | ErrorData.ProjectNotRestored d ->
                    projectNotRestoredLoaded.fire d.Project
                    Some (true, d.Project, ProjectLoadingState.NotRestored (d.Project, msg) )
                | ErrorData.ProjectParsingFailed d ->
                    Some (true, d.Project, ProjectLoadingState.Failed (d.Project, msg) )
                | _ ->
                    None

        match projStatus with
        | Some (isDone, path, state) ->
            updateInWorkspace path state
            loadedWorkspace |> Option.iter (workspaceChanged.fire)
            if isDone then setAnyProjectContext true
        | None ->
            ()

    let private initWorkspaceHelper parseVisibleTextEditors x  =
        clearLoadedProjects ()
        loadedWorkspace <- Some x
        workspaceChanged.fire x
        let projs =
            match x with
            | WorkspacePeekFound.Directory dir ->
                dir.Fsprojs
            | WorkspacePeekFound.Solution sln ->
                sln.Items
                |> Array.collect foldFsproj
                |> Array.map fst

        match x with
        | WorkspacePeekFound.Solution _
        | WorkspacePeekFound.Directory _ when not(projs |> Array.isEmpty) ->
            setAnyProjectContext true
        | _ -> ()

        let loadProjects =
            let loader =
                match workspaceNotificationAvaiable, getWorkspaceLoaderFromConfig () with
                | false, FSharpWorkspaceLoader.Projects ->
                    FSharpWorkspaceLoader.Projects
                | false, FSharpWorkspaceLoader.WorkspaceLoad ->
                    // workspaceLoad require notification, but registration failed => warning
                    // fallback to projects
                    FSharpWorkspaceLoader.Projects
                | true, loaderType -> loaderType

            match loader with
            | FSharpWorkspaceLoader.Projects ->
                Promise.executeForAll load
            | FSharpWorkspaceLoader.WorkspaceLoad ->
                LanguageService.workspaceLoad

        projs
        |> List.ofArray
        |> loadProjects
        |> Promise.bind (fun _ -> parseVisibleTextEditors ())
        |> Promise.map ignore

    let reinitWorkspace () =
        match loadedWorkspace with
        | None -> Promise.empty
        | Some wsp ->
            initWorkspaceHelper (fun _ -> Promise.empty) wsp

    let initWorkspace parseVisibleTextEditors =
        getWorkspaceModeFromConfig ()
        |> getWorkspaceForMode
        |> Promise.bind (function Some x -> Promise.lift x | None -> getWorkspaceForModeIonideSearch ())
        |> Promise.bind (initWorkspaceHelper parseVisibleTextEditors)

    let activate (context: ExtensionContext) parseVisibleTextEditors =
        commands.registerCommand("fsharp.clearCache", clearCache |> unbox<Func<obj,obj>> )
        |> context.subscriptions.Add

        workspaceNotificationAvaiable <- LanguageService.registerNotifyWorkspace handleProjectParsedNotification

        commands.registerCommand("fsharp.changeWorkspace", (fun _ ->
            workspacePeek ()
            |> Promise.bind pickFSACWorkspace
            |> Promise.bind (function Some w -> initWorkspaceHelper parseVisibleTextEditors w  | None -> Promise.empty )
            |> box
            ))
        |> context.subscriptions.Add

        initWorkspace parseVisibleTextEditors

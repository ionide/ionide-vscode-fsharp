namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers

open DTO
open System.Collections.Generic
module node = Fable.Import.Node.Exports


module Project =
    let private logger = ConsoleAndOutputChannelLogger(Some "Project", Level.DEBUG, None, Some Level.DEBUG)

    [<RequireQualifiedAccess>]
    type ProjectLoadingState =
        | Loading of path : string
        | Loaded of proj : Project
        | Failed of path : string * error : string
        | NotRestored of path : string * error : string
        | LanguageNotSupported of path: string

    let private emptyProjectsMap : Dictionary<ProjectFilePath,ProjectLoadingState> = Dictionary()

    let mutable private loadedProjects = emptyProjectsMap

    let mutable private loadedWorkspace : WorkspacePeekFound option = None

    let mutable workspaceNotificationAvaiable = false

    let setAnyProjectContext = Context.cachedSetter<bool> "fsharp.project.any"

    let private workspaceChangedEmitter = EventEmitter<WorkspacePeekFound>()
    let workspaceChanged = workspaceChangedEmitter.event

    let private projectNotRestoredLoadedEmitter = EventEmitter<string>()
    let projectNotRestoredLoaded = projectNotRestoredLoadedEmitter.event

    let private projectLoadedEmitter = EventEmitter<Project>()
    let projectLoaded = projectLoadedEmitter.event

    let private workspaceLoadedEmitter = EventEmitter<unit>()
    let workspaceLoaded = workspaceLoadedEmitter.event

    let private statusUpdatedEmitter = EventEmitter<unit>()
    let statusUpdated = statusUpdatedEmitter.event

    let excluded = "FSharp.excludeProjectDirectories" |> Configuration.get [| ".git"; "paket-files"; ".fable"; "packages"; "node_modules" |]

    let deepLevel = "FSharp.workspaceModePeekDeepLevel" |> Configuration.get 2 |> max 0

    let isANetCoreAppProject (project : Project) =
        let projectContent = (node.fs.readFileSync project.Project).toString()
        let netCoreTargets =
            [ "<TargetFramework>netcoreapp"
              "<Project Sdk=\"" ]

        let findInProject (toFind : string) =
            projectContent.IndexOf(toFind) >= 0

        netCoreTargets |> Seq.exists findInProject

    let isNetCoreApp (project : Project) =
        let projectContent = (node.fs.readFileSync project.Project).toString()
        let core = "<TargetFramework>netcoreapp"
        projectContent.IndexOf(core) >= 0

    let isSDKProject (project : Project) =
        match project.Info with
        | ProjectResponseInfo.DotnetSdk _ -> true
        | _ -> false

    let isSDKProjectPath (project : string) =
        let projectContent = (node.fs.readFileSync project).toString()
        let sdk = "<Project Sdk=\""
        projectContent.IndexOf(sdk) >= 0

    let isExeProject (project : Project) =
        match project.Output, isANetCoreAppProject project with
        | _, true ->
            project.OutputType.ToLowerInvariant() <> "lib"
        | out, _ when out |> String.endWith ".exe" -> true
        | _ -> false

    let getInWorkspace () =
        loadedProjects |> Seq.toList |> List.map (fun n -> n.Value)

    let tryFindInWorkspace (path : string) =
        loadedProjects |> Seq.tryFind (fun n -> n.Key = path.ToUpperInvariant ()) |> Option.map (fun n -> n.Value)

    let updateInWorkspace (path : string) state =
        let path = path.ToUpperInvariant ()
        match loadedProjects.TryGetValue path with
        | true, v ->
            match v, state  with
            | ProjectLoadingState.Loaded _, ProjectLoadingState.Loading _ -> ()
            | _ ->
                loadedProjects.Add(path, state)
                statusUpdatedEmitter.fire ()
        | _ ->
            loadedProjects.Add(path, state)
            statusUpdatedEmitter.fire ()


    let getProjectsFromWorkspacePeek () =
        match loadedWorkspace with
        | None -> []
        | Some (WorkspacePeekFound.Solution sln) ->
            let rec getProjs (item : WorkspacePeekFoundSolutionItem) =
                match item.Kind with
                | MsbuildFormat _proj ->
                    [|item.Name |]
                | Folder folder ->
                    folder.Items |> Array.collect getProjs
            sln.Items
            |> Array.collect getProjs
            |> Array.toList
        | Some(WorkspacePeekFound.Directory dir) ->
            dir.Fsprojs
            |> Array.toList


    let getNotLoaded () =
        let lst =
            getProjectsFromWorkspacePeek ()
            |> List.choose (fun n ->
                match tryFindInWorkspace n with
                | None -> Some n
                | Some _ -> None
            )
        lst

    let isIgnored (path: string) =
        let relativePath = node.path.relative (workspace.rootPath, path)

        let isSubDir p =
            let relativeToDir = node.path.relative(p, relativePath)
            let isSubdir = not (relativeToDir.StartsWith(".."))
            isSubdir

        excluded
        |> Array.exists isSubDir

    let private findAll () =
        let rec findProjs dir =
            let files = node.fs.readdirSync (U2.Case1 dir)
            files
            |> Seq.toList
            |> List.collect(fun s' ->
                try
                    let s = dir + node.path.sep + s'
                    if excluded |> Array.contains s' then
                        []
                    elif node.fs.statSync(U2.Case1 s).isDirectory () then
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
            let files = node.fs.readdirSync (U2.Case1 dir)
            files
            |> Seq.toList
            |> List.collect(fun s' ->
                try
                    let s = dir + node.path.sep + s'
                    if excluded |> Array.contains s' then []
                    elif node.fs.statSync(U2.Case1 s).isDirectory () then findProjs (s)
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

    let load comingFromRestore (path : string) =
        updateInWorkspace path (ProjectLoadingState.Loading path)

        let loaded (pr : ProjectResult) =
            if isNotNull pr then
                projectLoadedEmitter.fire (pr.Data)
                Some (pr.Data.Project, (ProjectLoadingState.Loaded pr.Data))
            else
                None

        let failed (msg : string, err : ErrorData) = 
            match err with
            | ErrorData.ProjectNotRestored _d when not comingFromRestore ->
                projectNotRestoredLoadedEmitter.fire path
                Some (path, ProjectLoadingState.NotRestored (path, msg) )
            | _ when not comingFromRestore && isSDKProjectPath path ->
                projectNotRestoredLoadedEmitter.fire path
                Some (path, (ProjectLoadingState.Failed (path, msg)))
            | _ ->
                Some (path, (ProjectLoadingState.Failed (path, msg)))

        if path.EndsWith ".fsproj" then
            LanguageService.project path
            |> Promise.map (fun r ->
                match r with
                | Ok proj -> loaded proj 
                | Error err -> failed err)
            |> Promise.map (fun r ->
                match r with
                | Some (path, state) ->
                    updateInWorkspace path state
                    loadedWorkspace |> Option.iter (workspaceChangedEmitter.fire)
                    setAnyProjectContext true
                | None ->
                    ())
        else
            Promise.empty

    let private chooseLoaded = function ProjectLoadingState.Loaded p -> Some p | _ -> None

    let getLoaded = getInWorkspace >> List.choose chooseLoaded

    let tryFindLoadedProjectByFile (filePath : string) =
        getLoaded ()
        |> List.tryPick (fun v ->
            let len =
                v.Files
                |> List.filter (fun f -> (f.ToUpperInvariant ()) = (filePath.ToUpperInvariant ()))
                |> List.length
            if len > 0 then Some v else None )

    let rec foldFsproj (item : WorkspacePeekFoundSolutionItem) =
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
            | Some (ProjectLoadingState.NotRestored _)
            | Some (ProjectLoadingState.LanguageNotSupported _ ) ->
                false

        projs
        |> Array.exists loadingInProgress

    let getLoadedSolution () = loadedWorkspace

    let getCaches () =
        let rec findProjs dir =
            let files = node.fs.readdirSync (U2.Case1 dir)
            files
            |> Seq.toList
            |> List.collect(fun s' ->
                try
                    let s = dir + node.path.sep + s'
                    if excluded |> Array.contains s' then
                        []
                    elif node.fs.statSync(U2.Case1 s).isDirectory () then
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
        cached |> Seq.iter (U2.Case1 >> node.fs.unlinkSync)
        window.showInformationMessage("Cache cleared")

    let countProjectsInSln (sln : WorkspacePeekFoundSolution) =
        sln.Items |> Array.map foldFsproj |> Array.sumBy Array.length

    [<RequireQualifiedAccess>]
    type ConfiguredWorkspace =
        | Solution of path: string
        | Directory of path: string

    module private CurrentWorkspaceConfiguration =
        let private key = "FSharp.workspacePath"

        let mutable extensionWorkspaceState: Memento option = None

        let setContext (context : ExtensionContext) =
            extensionWorkspaceState <- Some context.workspaceState

        let private parse (value: string) =
          let fullPath = path.resolve(workspace.rootPath, value)
          if value.ToLowerInvariant().EndsWith(".sln") then
              ConfiguredWorkspace.Solution fullPath
          else
              ConfiguredWorkspace.Directory fullPath

        let private pathExists (value: ConfiguredWorkspace) =
            match value with
            | ConfiguredWorkspace.Directory dir ->
                try
                    let stats = fs.statSync (U2.Case1 dir)
                    not (isNull stats) && (stats.isDirectory())
                with
                | _ -> false
            | ConfiguredWorkspace.Solution sln ->
                try
                    let stats = fs.statSync (U2.Case1 sln)
                    not (isNull stats) && (stats.isFile())
                with
                | _ -> false

        let private parseAndValidate (config: string option) =
            match config with
            | None
            | Some "" -> None
            | Some ws ->
                let configured = parse ws
                if pathExists configured then
                    Some configured
                else
                    logger.Warn("Ignoring configured workspace '%s' as the file or directory can't be resolved (From '%s')", ws, sprintf "%A" configured)
                    None

        let private getStringFromWorkspaceConfig () =
            workspace.getConfiguration().get<string option>(key)

        let private getStringFromExtensionState () =
            extensionWorkspaceState.Value.get<string>(key)

        let get () =
            match parseAndValidate (getStringFromWorkspaceConfig()) with
            | Some c -> Some c
            | None -> parseAndValidate (getStringFromExtensionState())

        let private getWorkspacePath (value: ConfiguredWorkspace) =
            match value with
            | ConfiguredWorkspace.Solution path -> path
            | ConfiguredWorkspace.Directory path -> path

        let private isConfiguredInWorkspace () =
            match getStringFromWorkspaceConfig () with
            | None
            | Some "" -> false
            | Some _ -> true

        let set (value: ConfiguredWorkspace) =
            let configuredPath = getWorkspacePath value
            if isConfiguredInWorkspace () then
                let relativePath =
                    let raw = path.relative(workspace.rootPath, configuredPath)
                    if not (path.isAbsolute raw) && not (raw.StartsWith "..") then
                        "./" + raw
                    else
                        raw

                let config = workspace.getConfiguration()
                config.update(key, relativePath, false)
            else
                extensionWorkspaceState.Value.update(key, configuredPath)

        let setFromPeek (value: WorkspacePeekFound) =
            match value with
            | WorkspacePeekFound.Directory dir -> ConfiguredWorkspace.Directory dir.Directory
            | WorkspacePeekFound.Solution sln -> ConfiguredWorkspace.Solution sln.Path
            |> set

        let private removeEndSlash (value: string) =
            if value.Length = 0 then
                ""
            else
                let lastChar = value.[value.Length - 1]
                if lastChar = '/' || lastChar = '\\' then
                    value.Substring(0, value.Length - 1)
                else
                    value

        let equalPeek (value: ConfiguredWorkspace) (peek: WorkspacePeekFound) =
            match value, peek with
            | ConfiguredWorkspace.Directory valueDir, WorkspacePeekFound.Directory peekDir ->
                removeEndSlash valueDir = removeEndSlash peekDir.Directory
            | ConfiguredWorkspace.Solution valueSln, WorkspacePeekFound.Solution peekSln ->
                valueSln = peekSln.Path
            | _ -> false

        let tryFind (value: ConfiguredWorkspace option) (found: WorkspacePeekFound list) =
            value |> Option.bind (fun configured ->
                if pathExists configured then
                    found |> List.tryFind (equalPeek configured)
                else
                    None)

    let pickFSACWorkspace (ws : WorkspacePeekFound list) (defaultPick: ConfiguredWorkspace option) =
        let isDefault (peekFound: WorkspacePeekFound) =
            match defaultPick with
            | Some pick -> CurrentWorkspaceConfiguration.equalPeek pick peekFound
            | None -> false

        let text (x : WorkspacePeekFound) =
            let check = if isDefault x then "✔ " else ""

            match x with
            | WorkspacePeekFound.Directory dir ->
                let item = createEmpty<QuickPickItem>
                item.label <- sprintf "%s%s" check dir.Directory
                item.description <- sprintf "Directory with %i projects" dir.Fsprojs.Length
                item
            | WorkspacePeekFound.Solution sln ->
                let relative = path.relative (workspace.rootPath, sln.Path)
                let item = createEmpty<QuickPickItem>
                item.label <- sprintf "%s%s" check relative
                item.description <- sprintf "Solution with %i projects" (countProjectsInSln sln)
                item

        match ws |> List.map (fun x -> (text x), x) with
        | [] ->
            None |> Promise.lift
        | projects ->
            promise {
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Workspace or Solution"
                let chooseFrom = projects |> List.map fst |> ResizeArray
                let! chosen = window.showQuickPick(chooseFrom |> U2.Case1, opts)
                if JS.isDefined chosen then
                    let selected = projects |> List.tryFind (fun (qp, _) -> qp = chosen) |> Option.map snd
                    match selected with
                    | Some selected ->
                        do! CurrentWorkspaceConfiguration.setFromPeek selected
                        return Some selected
                    | None -> return None
                else
                    return ws |> List.tryFind isDefault
            }

    let execWithDotnet outputChannel cmd =
        promise {
            let! dotnet = LanguageService.dotnet ()
            match dotnet with
            | Some dotnet -> return Process.spawnWithNotification dotnet "" cmd outputChannel
            | None -> return! Promise.reject "dotnet binary not found"
        }

    let exec exe outputChannel cmd =
        promise {
            return Process.spawnWithNotification exe "mono" cmd outputChannel
        }

    let private execWithDotnetWithShell cmd =
        promise {
            let! dotnet = LanguageService.dotnet ()
            match dotnet with
            | Some dotnet ->
                return Process.spawnWithShell dotnet "" cmd
            | None -> return! Promise.reject "dotnet binary not found"
        }

    let private execWithShell exe cmd =
        promise {
            return Process.spawnWithShell exe "mono" cmd
        }

    let getLauncher outputChannel (project : Project) =
        let execDotnet = fun args ->
            let cmd = "run -p " + (String.quote project.Project) + if String.IsNullOrEmpty args then "" else " -- " + args
            execWithDotnet outputChannel cmd
        match project.OutputType, isNetCoreApp project with
        | "exe", true -> Some execDotnet
        | "exe", _  -> Some (fun args -> exec project.Output outputChannel args)
        | _ , true -> Some execDotnet
        | _ -> None

    let getLauncherWithShell  (project : Project) =
        let execDotnet = fun args ->
            let cmd = "run -p " + (String.quote project.Project) + if String.IsNullOrEmpty args then "" else " -- " + args
            execWithDotnetWithShell cmd
        match project.OutputType, isNetCoreApp project with
        | "exe", true -> Some execDotnet
        | "exe", _ -> Some (fun args -> execWithShell project.Output args)
        | _, true -> Some execDotnet
        | _ -> None

    let private getWorkspaceForModeIonideSearch () =
        promise {
            let fsprojs = findAll ()
            let wdir =
                { WorkspacePeekFoundDirectory.Directory = vscode.workspace.rootPath
                  Fsprojs = fsprojs |> Array.ofList }
            return WorkspacePeekFound.Directory wdir
        }

    let private workspacePeek () =
        promise {
            if isNull vscode.workspace.rootPath then return []
            else
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

    let private getWorkspace () =
        promise {
            let! ws = workspacePeek ()
            let configured = CurrentWorkspaceConfiguration.get()
            let configuredPeek = CurrentWorkspaceConfiguration.tryFind configured ws
            match configuredPeek with
            | Some peek ->
                // If a workspace is configured, use it
                return Some peek
            | None ->
                // prefer the sln, load directly the first one, otherwise ask
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
                        pickFSACWorkspace ws None
                return choosen
        }

    let handleProjectParsedNotification res =
        let disableShowNotification = "FSharp.disableFailedProjectNotifications" |> Configuration.get false
        let projStatus =
            match res with
            | Choice1Of4 (pr: ProjectResult) ->
                projectLoadedEmitter.fire (pr.Data)
                Some (true, pr.Data.Project, (ProjectLoadingState.Loaded pr.Data))
            | Choice2Of4 (pr: ProjectLoadingResult) ->
                Some (false, pr.Data.Project, (ProjectLoadingState.Loading pr.Data.Project))
            | Choice3Of4 (msg, err) ->
                match err with
                | ErrorData.ProjectNotRestored d ->
                    projectNotRestoredLoadedEmitter.fire d.Project
                    Some (true, d.Project, ProjectLoadingState.NotRestored (d.Project, msg) )
                | ErrorData.ProjectParsingFailed d ->
                    Some (true, d.Project, ProjectLoadingState.Failed (d.Project, msg) )
                | ErrorData.LangugageNotSupported d ->
                    Some (true, d.Project, ProjectLoadingState.LanguageNotSupported(d.Project))
                | _ ->
                    if not disableShowNotification then
                        "Project loading failed"
                        |> vscode.window.showErrorMessage
                        |> ignore
                    None
            | Choice4Of4 msg ->
                match msg with
                | "finished" ->
                    workspaceLoadedEmitter.fire ()
                    None
                | _ -> None

        match projStatus with
        | Some (isDone, path, state) ->
            updateInWorkspace path state
            loadedWorkspace |> Option.iter (workspaceChangedEmitter.fire)
            if isDone then setAnyProjectContext true
        | None ->
            ()

    let private initWorkspaceHelper x  =
        clearLoadedProjects ()
        loadedWorkspace <- Some x
        workspaceChangedEmitter.fire x
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

        projs
        |> List.ofArray
        |> LanguageService.workspaceLoad
        |> Promise.map ignore


    let initWorkspace () =
        getWorkspace ()
        |> Promise.bind (function Some x -> Promise.lift x | None -> getWorkspaceForModeIonideSearch ())
        |> Promise.bind (initWorkspaceHelper)

    module internal ProjectStatus =
        let mutable timer = None
        let mutable path = ""

        let clearTimer () =
            match timer with
            | Some t ->
                clearTimeout t
                timer <- None
            | _ -> ()

        let mutable item : StatusBarItem option = None
        let private hideItem () = item |> Option.iter (fun n -> n.hide ())

        let private showItem (text : string) tooltip =
            path <- tooltip
            item.Value.text <- sprintf "$(flame) %s" text
            item.Value.tooltip <- tooltip
            item.Value.command <- "showProjStatusFromIndicator"
            item.Value.color <- ThemeColor "fsharp.statusBarWarnings" |> U2.Case2
            item.Value.show()


        let update () =
            let projs = getInWorkspace ()
            match projs |> List.tryPick (function | ProjectLoadingState.Failed(p,er) -> Some p | _ -> None  ) with
            | Some p -> showItem "Project loading failed" p
            | None ->
            match projs |> List.tryPick (function | ProjectLoadingState.Loading(p) -> Some p | _ -> None  ) with
            | Some p -> showItem "Project loading" p
            | None ->
            match projs |> List.tryPick (function | ProjectLoadingState.NotRestored(p,_) -> Some p | _ -> None  ) with
            | Some p -> showItem "Project not restored" p
            | None -> hideItem ()

        let statusUpdateHandler () =
            clearTimer()
            timer <- Some (setTimeout (fun () -> update ()) 1000.)




    let activate (context : ExtensionContext) =
        CurrentWorkspaceConfiguration.setContext context
        commands.registerCommand("fsharp.clearCache", clearCache |> unbox<Func<obj,obj>> )
        |> context.subscriptions.Add

        Notifications.notifyWorkspaceHandler <- Some handleProjectParsedNotification
        workspaceNotificationAvaiable <- true
        ProjectStatus.item <- Some (window.createStatusBarItem (StatusBarAlignment.Right, 9000. ))
        statusUpdated.Invoke(!!ProjectStatus.statusUpdateHandler) |> context.subscriptions.Add

        commands.registerCommand("fsharp.changeWorkspace", (fun _ ->
            workspacePeek ()
            |> Promise.bind (fun x -> pickFSACWorkspace x (CurrentWorkspaceConfiguration.get()))
            |> Promise.bind (function Some w -> initWorkspaceHelper w  | None -> Promise.empty )
            |> box
            ))
        |> context.subscriptions.Add

        commands.registerCommand("showProjStatusFromIndicator", (fun _ ->
            let name = path.basename (ProjectStatus.path)
            ShowStatus.CreateOrShow(ProjectStatus.path,name)
            |> box
        )) |> context.subscriptions.Add

        initWorkspace ()
        |> Promise.onSuccess (fun _ ->
            setTimeout (fun _ ->
                getNotLoaded ()
                |> List.iter (fun n -> load false n |> ignore)
            ) 1000.
            |> ignore
        )

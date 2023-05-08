namespace Ionide.VSCode.FSharp

open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open Ionide.VSCode.Helpers
open DTO

module node = Node.Api

[<RequireQualifiedAccess>]
module LaunchJsonVersion2 =

    type RequestLaunch =
        { name: string
          ``type``: string
          request: string
          preLaunchTask: string option
          program: string
          args: string array
          cwd: string
          console: string
          stopAtEntry: bool }

    let createRequestLaunch () =
        { RequestLaunch.name = ".NET Core Launch (console)"
          ``type`` = "coreclr"
          request = "launch"
          preLaunchTask = Some "build"
          program = "${workspaceRoot}/bin/Debug/{targetFramework}/{projectName}.dll"
          args = [||]
          cwd = "${workspaceRoot}"
          console = "externalTerminal"
          stopAtEntry = false }

    type RequestAttach =
        { name: string
          ``type``: string
          request: string
          processId: string }

    let createAttachLaunch () =
        { RequestAttach.name = ".NET Core Attach"
          ``type`` = "coreclr"
          request = "attach"
          processId = "${command:pickProcess}" }

    let assertVersion2 (cfg: WorkspaceConfiguration) =
        promise {
            do! cfg.update ("version", Some(box "0.2.0"), U2.Case2 false)
            do! cfg.update ("configurations", Some(box (ResizeArray<obj>())), U2.Case2 false)
        }

module Debugger =
    let outputChannel = window.createOutputChannel "Ionide: Debugger"

    let private logger =
        ConsoleAndOutputChannelLogger(Some "Debugger", Level.DEBUG, Some outputChannel, Some Level.DEBUG)

    let buildAndRun (project: Project) =
        promise {
            let! _ = MSBuild.buildProjectPath "Build" project
            let launcher = Project.getLauncherWithShell project

            match launcher with
            | None -> window.showWarningMessage ("Can't start project") |> ignore
            | Some l ->
                let! terminal = l []
                terminal.show ()
        }

    let setProgramPath project (cfg: LaunchJsonVersion2.RequestLaunch) =
        let relativeOutPath =
            node.path.relative(workspace.rootPath.Value, project.Output).Replace("\\", "/")

        let programPath = sprintf "${workspaceRoot}/%s" relativeOutPath

        // WORKAROUND the project.Output is the obj assembly, instead of bin assembly
        // ref https://github.com/fsharp/FsAutoComplete/issues/218
        let programPath = programPath.Replace("/obj/", "/bin/")
        cfg?cwd <- node.path.dirname project.Output
        cfg?program <- programPath

    let debuggerRuntime project = Some "coreclr"

    let debugProject (project: Project) (args: string[]) =
        promise {
            //TODO check if portablepdb, require info from FSAC

            let cfg = LaunchJsonVersion2.createRequestLaunch ()

            match debuggerRuntime project with
            | None -> window.showWarningMessage ("Can't start debugger") |> ignore
            | Some rntm ->
                cfg |> setProgramPath project
                cfg?``type`` <- rntm
                cfg?preLaunchTask <- None
                cfg?args <- args

                let debugConfiguration = cfg |> box |> unbox

                let folder = workspace.workspaceFolders.Value.[0]

                let! _ = debug.startDebugging (Some folder, U2.Case2 debugConfiguration)
                return ()
        }

    let buildAndDebug (project: Project) =
        promise {
            //TODO check if portablepdb, require info from FSAC

            let cfg = LaunchJsonVersion2.createRequestLaunch ()

            match debuggerRuntime project with
            | None -> window.showWarningMessage ("Can't start debugger") |> ignore
            | Some rntm ->
                cfg |> setProgramPath project
                cfg?``type`` <- rntm
                cfg?preLaunchTask <- None
                let debugConfiguration = cfg |> box |> unbox

                let folder = workspace.workspaceFolders.Value.[0]

                let! msbuildExit = MSBuild.buildProjectPath "Build" project

                match msbuildExit.Code with
                | Some code when code <> 0 ->
                    return! Promise.reject (exn $"msbuild 'Build' failed with exit code %i{code}")
                | _ ->
                    let! res = debug.startDebugging (Some folder, U2.Case2 debugConfiguration)
                    return ()
        }

    let mutable startup = None
    let mutable context: ExtensionContext option = None

    let setDefaultProject (project: Project) =
        startup <- Some project

        context
        |> Option.iter (fun c -> c.workspaceState.update ("defaultProject", Some(box project)) |> ignore)

    let chooseDefaultProject () =
        promise {
            match Project.getLoaded () with
            | [] -> return None
            | project :: [] -> return Some project
            | projects ->
                let picks =
                    projects
                    |> List.map (fun p -> createObj [ "data" ==> p; "label" ==> p.Project ])
                    |> ResizeArray

                let! proj = window.showQuickPick (unbox<U2<ResizeArray<QuickPickItem>, _>> picks)

                if JS.isDefined proj then
                    let project = unbox<Project> (proj?data)
                    setDefaultProject project
                    return Some project
                else
                    return None
        }

    let buildAndRunDefault () =
        match startup with
        | None -> chooseDefaultProject () |> Promise.map (Option.map (buildAndRun) >> ignore)
        | Some p -> buildAndRun p

    let buildAndDebugDefault () =
        match startup with
        | None -> chooseDefaultProject () |> Promise.map (Option.map (buildAndDebug) >> ignore)
        | Some p -> buildAndDebug p

    /// minimal set of properties from the launchsettings json
    [<Interface>]
    type JsMap<'t> =
        [<Emit("$0[$1]")>]
        abstract member Item: string -> 't

        [<Emit("Object.keys($0)")>]
        abstract member Keys: string[]

    [<Interface>]
    type LaunchSettingsConfiguration =
        abstract member commandName: string option
        abstract member commandLineArgs: string option
        abstract member executablePath: string option
        abstract member workingDirectory: string option
        abstract member launchBrowser: bool option
        abstract member launchUrl: bool option
        abstract member environmentVariables: JsMap<string>
        abstract member applicationUrl: string option

    [<Interface>]
    type LaunchSettingsFile =
        abstract member profiles: JsMap<LaunchSettingsConfiguration> option

    let readSettingsForProject (project: Project) =
        // todo: the subfolder is 'My Project' for VB, if we ever handle that
        let file =
            node.path.join (node.path.dirname (project.Project), "Properties", "launchSettings.json")

        let fileExists = node.fs.existsSync (U2.Case1 file)

        if fileExists then
            logger.Info $"Reading launch settings from %s{file}"
            let fileContent = node.fs.readFileSync file
            let settings: LaunchSettingsFile = Node.Util.JSON.parse (string fileContent)

            if
                JS.isDefined settings
                && Option.isSome settings.profiles
                && not (settings.profiles.Value.Keys.Length = 0)
            then
                logger.Info $"found {settings.profiles.Value.Keys.Length} profiles."
                Some settings.profiles.Value
            else
                logger.Info $"No profiles found in %s{file}"
                None
        else
            logger.Info $"No launch settings exist for project %s{project.Project}"
            None

    let makeDebugConfigFor
        (
            name: string,
            ls: LaunchSettingsConfiguration,
            project: Project,
            buildTaskOpt: Task option
        ) : DebugConfiguration option =
        if ls.commandName <> Some "Project" || ls.commandName = None then
            None
        else if project.OutputType <> "exe" then
            None
        else
            let projectName = node.path.basename (project.Project)
            let projectExecutable = project.Output

            let cliArgs = ls.commandLineArgs |> Option.defaultValue "" |> String.split [| ' ' |] // this is bad splitting - ideally we would get an args array from the json file...

            let c = createEmpty<DebugConfiguration>
            c.name <- $"{projectName}: {name}"
            c.``type`` <- "coreclr"
            c.request <- "launch"
            c?program <- projectExecutable
            c?args <- cliArgs

            match buildTaskOpt with
            | Some bt -> c?preLaunchTask <- $"Build: {bt.name}"
            | None -> ()

            c?cwd <- ls.workingDirectory |> Option.defaultValue "${workspaceFolder}"



            match ls.launchBrowser with
            | Some true ->
                c?serverReadyAction <-
                    {| action = "openExternally"
                       pattern = "\\bNow listening on:\\s+(https?://\\S+)" |} // TODO: make this pattern extendable?
            | _ -> ()

            if JS.isDefined ls.environmentVariables then
                let vars =
                    ls.environmentVariables.Keys
                    |> Array.choose (fun k ->
                        let value = ls.environmentVariables[k]

                        if JS.isDefined value then
                            let replaced = Environment.expand value
                            Some(k, box replaced)
                        else
                            None)

                c?env <- createObj vars

                if
                    not (JS.isDefined ls.environmentVariables["ASPNETCORE_URLS"])
                    && Option.isSome ls.applicationUrl
                then
                    c?env?ASPNETCORE_URLS <- ls.applicationUrl.Value

            c?console <- "internalConsole"
            c?stopAtEntry <- false
            let presentation = {| hidden = false; group = "ionide" |}

            c?presentation <- presentation

            Some c

    let configsForProject (project, launchSettings: JsMap<LaunchSettingsConfiguration>, buildTaskOpt) =
        seq {
            for name in launchSettings.Keys do
                logger.Info $"Making config for {name}"
                let settings: LaunchSettingsConfiguration = launchSettings[name]

                if JS.isDefined settings then
                    match makeDebugConfigFor (name, settings, project, buildTaskOpt) with
                    | Some cfg -> yield cfg
                    | None -> ()
                else
                    ()
        }

    let defaultConfigForProject (p: Project, buildTaskOpt: Task option) : DebugConfiguration option =
        if p.OutputType <> "exe" then
            None
        else
            let c = createEmpty<DebugConfiguration>
            c.name <- $"{node.path.basename p.Project}"
            c.``type`` <- "coreclr"
            c.request <- "launch"
            c?program <- p.Output

            c?cwd <- "${workspaceFolder}"

            c?console <- "internalConsole"
            c?stopAtEntry <- false

            match buildTaskOpt with
            | Some bt -> c?preLaunchTask <- $"Build: {bt.name}"
            | None -> ()

            let presentation = {| hidden = false; group = "ionide" |}

            c?presentation <- presentation
            Some c

    let launchSettingProvider =
        let msbuildTasksFilter: TaskFilter =
            let f = createEmpty<TaskFilter>
            f.``type`` <- Some "msbuild"
            f

        { new DebugConfigurationProvider with
            override x.provideDebugConfigurations(folder: option<WorkspaceFolder>, token: option<CancellationToken>) =
                let generate () =
                    promise {
                        logger.Info("Evaluating launch settings configurations for %O", folder)
                        let projects = Project.getLoaded ()
                        let! msbuildTasks = tasks.fetchTasks (msbuildTasksFilter)

                        let tasks =
                            projects
                            |> List.collect (fun (p: Project) ->
                                [ let projectFile = node.path.basename p.Project

                                  let buildTaskForProject =
                                      msbuildTasks
                                      |> Seq.tryFind (fun t ->
                                          t.group = Some vscode.TaskGroup.Build && t.name = projectFile)
                                  // emit configurations for any launchsettings for this project
                                  match readSettingsForProject p with
                                  | Some launchSettings ->
                                      yield! configsForProject (p, launchSettings, buildTaskForProject)
                                  | None -> ()
                                  // emit a default configuration for this project if it is an executable
                                  match defaultConfigForProject (p, buildTaskForProject) with
                                  | Some p -> yield p
                                  | None -> () ])

                        return ResizeArray tasks
                    }

                generate () // this bix/unbox is a hack because JS types
                |> box
                |> unbox

            override x.resolveDebugConfiguration
                (
                    folder: option<WorkspaceFolder>,
                    debugConfiguration: DebugConfiguration,
                    token: option<CancellationToken>
                ) =
                logger.Info("Resolving launch settings configuration %O for %O", debugConfiguration, folder)
                ProviderResult.Some(U2.Case1 debugConfiguration)

            override x.resolveDebugConfigurationWithSubstitutedVariables
                (
                    folder: option<WorkspaceFolder>,
                    debugConfiguration: DebugConfiguration,
                    token: option<CancellationToken>
                ) =
                logger.Info("Resolving subsituted launch settings configuration %O for %O", debugConfiguration, folder)
                ProviderResult.Some(U2.Case1 debugConfiguration) }

    let activate (c: ExtensionContext) =
        commands.registerCommand ("fsharp.runDefaultProject", (buildAndRunDefault) |> objfy2)
        |> c.Subscribe

        commands.registerCommand ("fsharp.debugDefaultProject", (buildAndDebugDefault) |> objfy2)
        |> c.Subscribe

        commands.registerCommand ("fsharp.chooseDefaultProject", (chooseDefaultProject) |> objfy2)
        |> c.Subscribe

        logger.Info "registering debug provider"

        debug.registerDebugConfigurationProvider (
            "coreclr",
            launchSettingProvider,
            DebugConfigurationProviderTriggerKind.Dynamic
        )
        |> c.Subscribe

        context <- Some c
        startup <- c.workspaceState.get<Project> "defaultProject"

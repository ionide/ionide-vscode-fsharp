namespace Ionide.VSCode.FSharp

module OTel =
    open Fable.Import.VSCode.Vscode
    open Node.Api
    open Node.Base

    let private logger =
        ConsoleAndOutputChannelLogger(Some "OTel", Level.DEBUG, None, Some Level.DEBUG)

    let private settingsString = "FSharp.openTelemetry"
    let mutable private isOTelEnabled = false

    let mutable private hasDocker = false

    let mutable private runningContainerId: string = null

    type SpawnResult =
        abstract pid: int
        abstract output: string[][]
        abstract stdout: string
        abstract stderr: string
        abstract status: int option
        abstract signal: string option
        abstract error: Error option

    let private runCommand command (args: string[]) =
        let options =
            {| cwd = None
               env = None
               encoding = "utf8"
               shell = true |}

        let result =
            childProcess.spawnSync (command, ResizeArray args, options) :?> SpawnResult

        match result.error with
        | Some err ->
            logger.Error("Error running command %s: %s", command, err.message)
            failwithf "%A" err
        | None -> result

    let private startContainer () =
        let result =
            runCommand
                "docker"
                [| "run"
                   "-d"
                   "--rm"
                   "-it"
                   "-p"
                   "18888:18888"
                   "-p"
                   "4317:18889"
                   "mcr.microsoft.com/dotnet/nightly/aspire-dashboard:8.0.0-preview.4" |]

        logger.Info("Container started with result %j", result)
        runningContainerId <- result.stdout
        isOTelEnabled <- true

    let enableOTelListener () =
        isOTelEnabled <- true

        if hasDocker then
            startContainer ()
            logger.Info("OpenTelemetry listener enabled")
            ()
        else
            logger.Warn("OpenTelemetry listener requires Docker to be installed") |> ignore

            ()

    let stopContainer (containerId: string) =
        runCommand "docker" [| "stop"; containerId |] |> ignore

    let disableOTelListener () =
        isOTelEnabled <- false

        if hasDocker && not (System.String.IsNullOrEmpty runningContainerId) then
            stopContainer runningContainerId |> ignore
            logger.Info("OpenTelemetry listener disabled")
            ()

    let detectDocker () =
        logger.Info("detecting presence of docker")
        hasDocker <- true

    let activate (ctx: ExtensionContext) =
        detectDocker ()

        let settings = workspace.getConfiguration settingsString
        let newEnablement = settings.get<bool> ("enabled")

        logger.Info("Configuration value is %j", newEnablement)

        match isOTelEnabled, newEnablement with
        | true, (Some false | None) -> disableOTelListener ()
        | false, Some true -> enableOTelListener ()
        | _, _ -> ()

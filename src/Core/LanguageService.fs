namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers
open Fable.Import.ws


open DTO
open Fable.Import.Axios

module URL =
    /// Create a new URL from a url string
    [<Emit("new URL($0)")>]
    let create (urlString: string) : Fable.Import.Browser.URL = jsNative ""

    /// Create a new URL with a relative path from some base URL
    [<Emit("new URL($0, $1)")>]
    let createFrom (relativeInput: string) (baseURL: Fable.Import.Browser.URL): Fable.Import.Browser.URL = jsNative ""

module LanguageService =

    let ax =  Globals.require.Invoke "axios" |> unbox<Axios.AxiosStatic>

    [<RequireQualifiedAccess>]
    type LogConfigSetting =
        | None
        | Output
        | DevConsole
        | Both

    let logLanguageServiceRequestsConfigSetting =
        try
            match "FSharp.logLanguageServiceRequests" |> Configuration.get "output" with
            | "devconsole" -> LogConfigSetting.DevConsole
            | "output" -> LogConfigSetting.Output
            | "both" -> LogConfigSetting.Both
            | _ -> LogConfigSetting.Output
        with
        | _ -> LogConfigSetting.Output

    // note: always log to the loggers, and let it decide where/if to write the message
    let createConfiguredLoggers source channelName =
        let logLanguageServiceRequestsOutputWindowLevel () =
            try
                match "FSharp.logLanguageServiceRequestsOutputWindowLevel" |> Configuration.get "INFO" with
                | "DEBUG" -> Level.DEBUG
                | "INFO" -> Level.INFO
                | "WARN" -> Level.WARN
                | "ERROR" -> Level.ERROR
                | _ -> Level.INFO
            with
            | _ -> Level.INFO

        let channel, logRequestsToConsole =
            match logLanguageServiceRequestsConfigSetting with
            | LogConfigSetting.None -> None, false
            | LogConfigSetting.Both -> Some (window.createOutputChannel channelName), true
            | LogConfigSetting.DevConsole -> None, true
            | LogConfigSetting.Output -> Some (window.createOutputChannel channelName), false

        let logLevel = logLanguageServiceRequestsOutputWindowLevel ()
        let editorSideLogger = ConsoleAndOutputChannelLogger(Some source, logLevel, channel, Some logLevel)
        let mutable minLogLevel = logLevel

        let showCurrentLevel level =
            if level <> Level.DEBUG then
                editorSideLogger.Info ("Logging to output at level %s. If you want detailed messages, try level DEBUG.", (level.ToString()))

        editorSideLogger.ChanMinLevel |> showCurrentLevel

        vscode.workspace.onDidChangeConfiguration
        |> Event.invoke (fun _ ->
            editorSideLogger.ChanMinLevel <- logLanguageServiceRequestsOutputWindowLevel ()
            minLogLevel <- logLanguageServiceRequestsOutputWindowLevel ()
            editorSideLogger.ChanMinLevel |> showCurrentLevel )
        |> ignore

        // show the stdout data printed from FSAC in a separate channel
        let fsacStdOutWriter =
            if logRequestsToConsole then
                let chan = window.createOutputChannel (channelName + " (server)")
                let chan2 = window.createOutputChannel (channelName + " (Symbol Cache)")
                (fun (s : string) ->
                    if s.Contains "[Symbol Cache]" then
                        if minLogLevel <> DEBUG && s.Contains "[Debug]" then () else chan2.append s
                    else
                        if minLogLevel <> DEBUG && s.Contains "[Debug]" then () else chan.append s)
            else
                ignore

        editorSideLogger, fsacStdOutWriter

    let log, fsacStdoutWriter = createConfiguredLoggers "IONIDE-FSAC" "F# Language Service"

    let private genPort () =
        let r = JS.Math.random ()
        let r' = r * (8999. - 8100.) + 8100.
        r'.ToString().Substring(0,4)

    let private shouldStartFSAC, fsacUrl =
        match Configuration.tryGet "FSharp.fsacUrl" with
        | Some fsacUrlConfig ->
            let url = URL.create fsacUrlConfig
            log.Info("FSAC Url was provided by configuration and is %s", url)
            false, url
        | None ->
            let port = genPort()
            let url = URL.create (sprintf "http://127.0.0.1:%s" port)
            log.Info ("No FSAC url provided, using %s", url)
            true, url


    let private url fsacAction requestId =
        let baseUrl = URL.createFrom (sprintf "/%s" fsacAction) fsacUrl
        baseUrl.search <- sprintf "requestId=%d" requestId
        baseUrl.toString ()

    /// because node 7.x doesn't give us the signal used if a process dies, we have to set up our own signal to show if we died via our own `stop()` call.
    let mutable private exitRequested : bool = false

    let mutable private service : ChildProcess.ChildProcess option =  None

    let mutable private socketNotify : WebSocket option = None

    let mutable private socketNotifyWorkspace : WebSocket option = None

    let mutable private socketNotifyAnalyzer : WebSocket option = None

    let private platformPathSeparator = if Process.isMono () then "/" else "\\"

    let private makeRequestId =
        let mutable requestId = 0
        fun () -> (requestId <- requestId + 1); requestId

    let private relativePathForDisplay (path : string) =
        path.Replace(vscode.workspace.rootPath + platformPathSeparator, "~" + platformPathSeparator)

    let private makeOutgoingLogPrefix (requestId : int) = String.Format("REQ ({0:000}) ->", requestId)
    let private makeIncomingLogPrefix (requestId : int) = String.Format("RES ({0:000}) <-", requestId)

    let private logOutgoingRequest requestId (fsacAction : string) obj =
        // At the INFO level, it's nice to see only the key data to get an overview of
        // what's happening, without being bombarded with too much detail
        let extraPropInfo =
            if JS.isDefined (obj?FileName) then Some ", File = \"%s\"", Some (relativePathForDisplay (obj?FileName |> unbox))
            elif JS.isDefined (obj?Project) then Some ", Project = \"%s\"", Some (relativePathForDisplay (obj?Project |> unbox))
            elif JS.isDefined (obj?Symbol) then Some ", Symbol = \"%s\"", Some (obj?Symbol |> unbox)
            else None, None

        match extraPropInfo with
        | None, None -> log.Debug (makeOutgoingLogPrefix(requestId) + " {%s}\nData=%j", fsacAction, obj)
        | Some extraTmpl, Some extraArg -> log.Debug (makeOutgoingLogPrefix(requestId) + " {%s}" + extraTmpl + "\nData=%j", fsacAction, extraArg, obj)
        | _, _ -> failwithf "cannot happen %A" extraPropInfo

    let private logIncomingResponse requestId fsacAction (started : DateTime) (r : Axios.AxiosXHR<_>) (res : _ option) (ex : exn option) =
        let elapsed = DateTime.Now - started
        match res, ex with
        | Some res, None ->
            log.Debug(makeIncomingLogPrefix(requestId) + " {%s} in %s ms: Kind={\"%s\"}\nData=%j", fsacAction, elapsed.TotalMilliseconds, res?Kind, res?Data)
        | None, Some ex ->
            log.Error (makeIncomingLogPrefix(requestId) + " {%s} ERROR in %s ms: {%j}, Data=%j", fsacAction, elapsed.TotalMilliseconds, ex.ToString(), obj)
        | _, _ -> log.Error(makeIncomingLogPrefix(requestId) + " {%s} ERROR in %s ms: %j, %j, %j", fsacAction, elapsed.TotalMilliseconds, res, ex.ToString(), obj)

    let private logIncomingResponseError requestId fsacAction (started : DateTime) (r : obj) =
        let elapsed = DateTime.Now - started
        log.Error (makeIncomingLogPrefix(requestId) + " {%s} ERROR in %s ms: %s Data=%j",
                    fsacAction, elapsed.TotalMilliseconds, r.ToString(), obj)

    type FSACResponse<'b> =
        | Error of string * ErrorData
        | Info of obj
        | Kind of string * 'b
        | Invalid

    let parseError (err : obj) =
        let data =
            match err?Code |> unbox with
            | ErrorCodes.GenericError ->
                ErrorData.GenericError
            | ErrorCodes.ProjectNotRestored ->
                ErrorData.ProjectNotRestored (err?AdditionalData |> unbox)
            | ErrorCodes.ProjectParsingFailed ->
                ErrorData.ProjectParsingFailed (err?AdditionalData |> unbox)
            | unknown ->
                //todo log not recognized for Debug
                ErrorData.GenericError
        (err?Message |> unbox<string>), data

    let prettyPrintError fsacAction (msg : string) (err : ErrorData) =
        let whenMsg =
            match fsacAction with
            | "project" -> "Project loading failed"
            | a -> sprintf "Cannot execute %s" a

        let d =
            match err with
            | ErrorData.GenericError ->
                ""
            | ErrorData.ProjectParsingFailed data ->
                sprintf "'%s'" data.Project
            | ErrorData.ProjectNotRestored data ->
                sprintf "'%s'" data.Project
        sprintf "%s, %s %s" whenMsg msg d

    let private requestRaw<'a, 'b> (fsacAction : string) id requestId (obj : 'a) =
        let started = DateTime.Now
        let fullRequestUrl = url fsacAction requestId
        logOutgoingRequest requestId fsacAction obj

        let options = createEmpty<AxiosXHRConfigBase<obj>>
        options.proxy <- Some (U2.Case2 false)
        ax.post (fullRequestUrl, obj, options)
        |> Promise.onFail (fun r ->
            // The outgoing request was not made
            logIncomingResponseError requestId fsacAction started r
            null |> unbox
        )
        |> Promise.map(fun r ->
            // the outgoing request was made
            try
                let resObj = (r.data |> unbox<string[]>).[id] |> JS.JSON.parse
                let res = resObj |> unbox<'b>
                logIncomingResponse requestId fsacAction started r (Some res) None
                if res?Kind |> unbox = "error" then FSACResponse.Error (res?Data |> parseError)
                elif res?Kind |> unbox = "info" then FSACResponse.Info (res?Data |> unbox)
                else FSACResponse.Kind ((res?Kind |> unbox), res)
            with
            | ex ->
                logIncomingResponse requestId fsacAction started r None (Some ex)
                FSACResponse.Invalid
        )

    let private request<'a, 'b> (fsacAction : string) id requestId (obj : 'a) =
         requestRaw fsacAction id requestId obj
         |> Promise.map(fun (r : FSACResponse<'b>) ->
             match r with
             | FSACResponse.Error (msg, err) ->
                log.Error (prettyPrintError fsacAction msg err)
                null |> unbox
             | FSACResponse.Info err -> null |> unbox
             | FSACResponse.Kind (t, res) -> res
             | FSACResponse.Invalid -> null |> unbox
          )

    let private requestCanFail<'a, 'b> (fsacAction : string) id requestId (obj : 'a) =
         requestRaw fsacAction id requestId obj
         |> Promise.bind(fun (r : FSACResponse<'b>) ->
             match r with
             | FSACResponse.Error (msg, err) ->
                log.Error (prettyPrintError fsacAction msg err)
                Promise.reject (msg, err)
             | FSACResponse.Kind (t, res) ->
                Promise.lift res
             | FSACResponse.Info _
             | FSACResponse.Invalid ->
                Promise.lift (null |> unbox)
          )

    let private handleUntitled (fn : string) = if fn.EndsWith ".fs" || fn.EndsWith ".fsi" || fn.EndsWith ".fsx" then fn else (fn + ".fsx")

    let private deserializeProjectResult (res : ProjectResult) =
        let parseInfo (f : obj) =
            match f?SdkType |> unbox with
            | "dotnet/sdk" ->
                ProjectResponseInfo.DotnetSdk (f?Data |> unbox)
            | "verbose" ->
                ProjectResponseInfo.Verbose
            | "project.json" ->
                ProjectResponseInfo.ProjectJson
            | _ ->
                log.Error("error during parsing of ProjectResult, invalid %j", f)
                f |> unbox

        { res with
            Data = { res.Data with
                        Info = parseInfo(res.Data.Info) } }

    let project s =
        { ProjectRequest.FileName = s }
        |> requestCanFail "project" 0 (makeRequestId())
        |> Promise.map deserializeProjectResult
        |> Promise.onFail(fun _ ->
            let msg = "Project parsing failed: " + path.basename(s)
            vscode.window.showErrorMessage(msg, "Show status")
            |> Promise.map(fun res ->
                if res = "Show status" then
                    ShowStatus.CreateOrShow(s, (path.basename(s)))
            )
            |> ignore
        )

    let parse path (text : string) (version : float) =
        let lines = text.Replace("\uFEFF", "").Split('\n')
        { ParseRequest.FileName = handleUntitled path
          ParseRequest.Lines = lines
          ParseRequest.IsAsync = true
          ParseRequest.Version = int version }
        |> request "parse" 1 (makeRequestId())

    let helptext s =
        { HelptextRequest.Symbol = s }
        |> request "helptext" 0 (makeRequestId())

    let completion fn sl line col keywords external version =
        { CompletionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "Contains"; SourceLine = sl; IncludeKeywords = keywords; IncludeExternal = external; Version = version }
        |> request "completion" 1 (makeRequestId())

    let symbolUse fn line col =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request "symboluse" 0 (makeRequestId())

    let symbolUseProject fn line col =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request "symboluseproject" 0 (makeRequestId())

    let symbolImplementationProject fn line col =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request "symbolimplementation" 0 (makeRequestId())

    let methods fn line col =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request "methods" 0 (makeRequestId())

    let tooltip fn line col =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request "tooltip" 0 (makeRequestId())

    let documentation fn line col =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request "documentation" 0 (makeRequestId())

    let toolbar fn line col =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request "tooltip" 0 (makeRequestId())

    let signature fn line col =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request<_, Result<string>> "signature" 0 (makeRequestId())

    let findDeclaration fn line col =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request "finddeclaration" 0 (makeRequestId())

    let findTypeDeclaration fn line col =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request "findtypedeclaration" 0 (makeRequestId())

    let f1Help fn line col : JS.Promise<Result<string>> =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request "help" 0 (makeRequestId())

    let signatureData fn line col =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request "signatureData" 0 (makeRequestId())

    let declarations fn (text : string) version =
        let lines = text.Replace("\uFEFF", "").Split('\n')
        { DeclarationsRequest.FileName = handleUntitled fn; Lines = lines; Version = version }
        |> request<_, Result<Symbols[]>> "declarations" 0 (makeRequestId())

    let declarationsProjects () =
        "" |> request "declarationsProjects" 0 (makeRequestId())

    let compilerLocation () =
        "" |> request<string, CompilerLocationResult> "compilerlocation" 0 (makeRequestId())

    let lint s =
        { ProjectRequest.FileName = s }
        |> request "lint" 0 (makeRequestId())

    let resolveNamespaces fn line col =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request "namespaces" 0 (makeRequestId())

    let unionCaseGenerator fn line col : JS.Promise<Result<UnionCaseGenerator>> =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request "unionCaseGenerator" 0 (makeRequestId())

    let recordStubGenerator fn line col : JS.Promise<Result<RecordStubCaseGenerator>> =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request "recordStubGenerator" 0 (makeRequestId())

    let interfaceStubGenerator fn line col : JS.Promise<Result<InterfaceStubGenerator>> =
        { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        |> request "interfaceStubGenerator" 0 (makeRequestId())

    let workspacePeek dir deep excludedDirs =
        let rec mapItem (f : WorkspacePeekFoundSolutionItem) : WorkspacePeekFoundSolutionItem option =
            let mapItemKind (i : obj) : WorkspacePeekFoundSolutionItemKind option =
                let data = i?Data
                match i?Kind |> unbox with
                | "folder" ->
                    let folderData : WorkspacePeekFoundSolutionItemKindFolder = data |> unbox
                    let folder : WorkspacePeekFoundSolutionItemKindFolder =
                        { Files = folderData.Files
                          Items = folderData.Items |> Array.choose mapItem }
                    Some (WorkspacePeekFoundSolutionItemKind.Folder folder)
                | "msbuildFormat" ->
                    Some (WorkspacePeekFoundSolutionItemKind.MsbuildFormat (data |> unbox))
                | _ ->
                    None
            match mapItemKind f.Kind with
            | Some kind ->
                Some
                   { WorkspacePeekFoundSolutionItem.Guid = f.Guid
                     Name = f.Name
                     Kind = kind }
            | None -> None

        let mapFound (f : obj) : WorkspacePeekFound option =
            let data = f?Data
            match f?Type |> unbox with
            | "directory" ->
                Some (WorkspacePeekFound.Directory (data |> unbox))
            | "solution" ->
                let sln =
                    { WorkspacePeekFoundSolution.Path = data?Path |> unbox
                      Configurations = data?Configurations |> unbox
                      Items = data?Items |> unbox |> Array.choose mapItem }
                Some (WorkspacePeekFound.Solution sln)
            | _ ->
                None

        let parse (ws : obj) =
            { WorkspacePeek.Found = ws?Found |> unbox |> Array.choose mapFound }

        { WorkspacePeekRequest.Directory = dir; Deep = deep; ExcludedDirs = excludedDirs |> Array.ofList }
        |> request "workspacePeek" 0 (makeRequestId())
        |> Promise.map (fun res -> parse (res?Data |> unbox))

    let workspaceLoad disableInMemoryProject projects  =
        { WorkspaceLoadRequest.Files = projects |> List.toArray; DisableInMemoryProjectReferences = disableInMemoryProject }
        |> request "workspaceLoad" 0 (makeRequestId())

    let unusedDeclarations s =
        { ProjectRequest.FileName = s }
        |> request "unusedDeclarations" 0 (makeRequestId())

    let unusedOpens s =
        { ProjectRequest.FileName = s }
        |> request "unusedOpens" 0 (makeRequestId())

    let simplifiedNames s =
        { ProjectRequest.FileName = s }
        |> request "simplifiedNames" 0 (makeRequestId())

    let projectsInBackground s =
        { ProjectRequest.FileName = s }
        |> request "projectsInBackground" 0 (makeRequestId())

    let compile s =
        { ProjectRequest.FileName = s }
        |> request "compile" 0 (makeRequestId())

    let enableSymbolCache () =
        "" |> request "enableSymbolCache" 0 (makeRequestId())

    let buildBackgroundSymbolCache () =
        "" |> request "buildBackgroundSymbolCache" 0 (makeRequestId())

    let registerAnalyzer s =
        { ProjectRequest.FileName = s }
        |> request "registerAnalyzer" 0 (makeRequestId())

    let private fsacConfig () =
        compilerLocation ()
        |> Promise.map (fun c -> c.Data)

    let private fileExists (path: string): JS.Promise<bool> =
        Promise.create(fun success _failure ->
            fs.access(!^path, fs.constants.F_OK, fun err -> success(err.IsNone)))

    let private getAnyCpuFsiPathFromCompilerLocation (location: CompilerLocation) = promise {
        match location.Fsi with
        | Some fsi ->
            // Only rewrite if FSAC returned 'fsi.exe' (For future-proofing)
            if path.basename fsi = "fsi.exe" then
                // If there is an anyCpu variant in the same dir we do the rewrite
                let anyCpuFile = path.join [| path.dirname fsi; "fsiAnyCpu.exe"|]
                let! anyCpuExists = fileExists anyCpuFile
                if anyCpuExists then
                    return Some anyCpuFile
                else
                    return Some fsi
            else
                return Some fsi
        | None ->
            return None
    }

    let fsi () =
        promise {
            match Environment.configFSIPath with
            | Some path -> return Some path
            | None ->
                let! fsacPaths = fsacConfig ()
                let! fsiPath = getAnyCpuFsiPathFromCompilerLocation fsacPaths
                return fsiPath
        }

    let fsc () =
        promise {
            match Environment.configFSCPath with
            | Some path -> return Some path
            | None ->
                let! fsacPaths = fsacConfig ()
                return fsacPaths.Fsc
        }

    let msbuild () =
        promise {
            match Environment.configMSBuildPath with
            | Some path -> return Some path
            | None ->
                let! fsacPaths = fsacConfig ()
                return fsacPaths.MSBuild
        }

    [<PassGenerics>]
    let private registerNotifyAll (cb : 'a -> unit) (ws : WebSocket) =
        ws.on_message((fun (res : string) ->
            log.Debug(sprintf "WebSocket message: '%s'" res)
            let n = res |> JS.JSON.parse
            cb (n |> unbox)
            ) |> unbox) |> ignore
        ()

    [<PassGenerics>]
    let registerNotify (cb : ParseResult -> unit) =
        let onParseResult n =
            if unbox n?Kind = "errors" then
                n |> unbox |> cb
        socketNotify
        |> Option.iter (registerNotifyAll onParseResult)

    [<PassGenerics>]
    let registerNotifyAnalyzer (cb : AnalyzerResult -> unit) =
        let onParseResult n =
            if unbox n?Kind = "analyzer" then
                n |> unbox |> cb
        socketNotifyAnalyzer
        |> Option.iter (registerNotifyAll onParseResult)

    [<PassGenerics>]
    let registerNotifyWorkspace (cb : _ -> unit) =
        let onMessage res =
            match res?Kind |> unbox with
            | "project" ->
                res |> unbox<ProjectResult> |> deserializeProjectResult |> Choice1Of4 |> cb
            | "projectLoading" ->
                res |> unbox<ProjectLoadingResult> |> Choice2Of4 |> cb
            | "error" ->
                res?Data |> parseError |> Choice3Of4 |> cb
            | "workspaceLoad" ->
                res?Data?Status |> unbox<string> |> Choice4Of4 |> cb
            | _ ->
                ()

        match socketNotifyWorkspace with
        | None -> false
        | Some ws ->
            ws |> registerNotifyAll onMessage
            true

    let private startSocket notificationEvent =
        let baseAddress = URL.createFrom (sprintf "/%s" notificationEvent) fsacUrl
        baseAddress.protocol <- "ws"
        let address = baseAddress.toString()
        try
            let sck = WebSocket address
            log.Info(sprintf "listening notification on /%s started" notificationEvent)
            Some sck
        with
        | e ->
            log.Error(sprintf "notification /%s initialization error: %s" notificationEvent e.Message)
            None

    let start' fsacExe (fsacArgs : string list) =
        Promise.create (fun resolve reject ->
            let child =
                let spawnLogged path (args: string list) =
                    fsacStdoutWriter (sprintf "Running: %s %s\n" path (args |> String.concat " "))
                    childProcess.spawn(path, args |> ResizeArray)
                spawnLogged fsacExe
                  [ yield! fsacArgs
                    yield! ["--mode"; "http"]
                    yield! ["--port"; fsacUrl.port]
                    yield sprintf "--hostPID=%i" (int Globals.``process``.pid) ]

            let mutable isResolvedAsStarted = false
            child
            |> Process.onOutput (fun buffer ->
                let outputString = buffer.toString()
                // Wait until FsAC sends the 'listener started' magic string until
                // we inform the caller that it's ready to accept requests.
                let isStartedMessage = outputString.Contains "listener started in"
                if isStartedMessage && not isResolvedAsStarted then
                    fsacStdoutWriter ("Resolving startup promise because FSAC printed the 'listener started' message")
                    fsacStdoutWriter "\n"
                    service <- Some child
                    resolve child
                    isResolvedAsStarted <- true

                fsacStdoutWriter outputString
            )
            |> Process.onError (fun e ->
                let error = unbox<JS.Error> e
                fsacStdoutWriter (error.message)
                if not isResolvedAsStarted then
                    reject (error.message)
            )
            |> Process.onErrorOutput (fun n ->
                let buffer = unbox<Buffer.Buffer> n
                fsacStdoutWriter (buffer.toString())
                if not isResolvedAsStarted then
                    reject (buffer.toString())
            )
            |> ignore
        )
        |> Promise.onFail (fun err ->
            log.Error("Failed to start language services. %s", err)
            if Process.isMono () then
                "Failed to start language services. Please check if mono is in PATH"
            else
                "Failed to start language services. Please check if Microsoft Build Tools 2013 are installed"
            |> vscode.window.showErrorMessage
            |> ignore)

    type [<RequireQualifiedAccess>] FSACTargetRuntime =
        | NET
        | NetcoreFdd

    type ConfigValue<'a> =
        | UserSpecified of 'a
        | Implied of 'a

    let runtimeSettingsKey = "FSharp.fsacRuntime"

    let targetRuntime: ConfigValue<FSACTargetRuntime> =
        let configured = Configuration.tryGet runtimeSettingsKey

        match configured with
        | Some "netcore" ->
            log.Info("Netcore runtime specified")
            UserSpecified FSACTargetRuntime.NetcoreFdd
        | Some "net" ->
            log.Info(".NET runtime specified")
            UserSpecified FSACTargetRuntime.NET
        | Some v ->
            log.Warn("Unknown configured runtime '%s', defaulting to .NET", v)
            Implied FSACTargetRuntime.NET
        | None ->
            log.Info("No runtime specified, defaulting to .NET")
            Implied FSACTargetRuntime.NET

    let setRuntime runtime =
        let value =
            match runtime with
            | FSACTargetRuntime.NET -> "net"
            | FSACTargetRuntime.NetcoreFdd -> "netcore"
        Configuration.set runtimeSettingsKey value
        |> Promise.bind (fun _ -> vscode.window.showInformationMessage("Please reload your VSCode instance for this change to take effect"))
        |> Promise.map (fun _ -> ())

    let spawnFSACForRuntime (runtime: ConfigValue<FSACTargetRuntime>) rootPath =
        let spawnNetFSAC mono =
            let path = rootPath + "/bin/fsautocomplete.exe"
            let fsacExe, fsacArgs =
                if Process.isMono () then
                    mono, [ yield path ]
                else
                    path, []
            start' fsacExe fsacArgs

        let spawnNetcoreFSAC dotnet =
            let path = rootPath + "/bin_netcore/fsautocomplete.dll"
            start' dotnet [ path ]

        let suggestNet () =
            promise {
                let! result = vscode.window.showInformationMessage("Consider using the .NET Framework/Mono language services by setting `FSharp.fsacRuntime` to `net` and installing .NET/Mono as appropriate for your system.", "Use .Net Framework")
                match result with
                | "Use .Net Framework" -> do! setRuntime FSACTargetRuntime.NET
                | _ -> ()
            }

        let suggestNetCore () = promise {
            let! result = vscode.window.showInformationMessage("Consider using the .NET Core language services by setting `FSharp.fsacRuntime` to `netcore`", "Use .Net Core")
            match result with
            | "Use .Net Core" -> do! setRuntime FSACTargetRuntime.NetcoreFdd
            | _ -> ()
        }

        let monoNotFound () = promise {
            let msg = """
            Cannot start .NET Framework/Mono language services because `mono` was not found.
            Consider:
            * setting the `FSharp.monoPath` settings key to a `mono` binary,
            * including `mono` in your PATH, or
            * installing the .NET Core SDK and using the `FSharp.fsacRuntime` `netcore` language settings
            """
            let! result = vscode.window.showErrorMessage(msg, "Use .Net Core")
            let! _ =
                match result with
                | "Use .Net Core" -> setRuntime FSACTargetRuntime.NetcoreFdd
                | _ -> promise.Return ()
            return failwith "no `mono` binary found"
        }

        let dotnetNotFound () = promise {
            let msg = """
            Cannot start .NET Core language services because `dotnet` was not found.
            Consider:
            * setting the `FSharp.dotnetLocation` settings key to a `dotnet` binary,
            * including `dotnet` in your PATH,
            * installing .NET Core into one of the default locations, or
            * using the `net` `FSharp.fsacRuntime` to use mono instead
            """
            let! result = vscode.window.showErrorMessage(msg, "Use .Net Framework")
            let! _ =
                match result with
                | "Use .Net Framework" -> setRuntime FSACTargetRuntime.NET
                | _ -> promise.Return ()
            return failwith "no `dotnet` binary found"
        }

        promise {
            let! mono = Environment.mono
            let! dotnet = Environment.dotnet
            log.Info(sprintf "finding FSAC for\n\truntime: %O\n\tmono: %O\n\tdotnet: %O" runtime mono dotnet)


            // The matrix here is a 2x3 table: .Net/.Net Core target on one axis, Windows/Mono/Dotnet execution environment on the other
            match runtime, mono, dotnet with
            // for any configuration, if the user specifies the framework to use do not suggest another framework for them

            // .Net framework handling
            | UserSpecified FSACTargetRuntime.NET, None, _ when Environment.isWin ->
                return! spawnNetFSAC ""
            | UserSpecified FSACTargetRuntime.NET, Some mono, _ ->
                return! spawnNetFSAC mono
            | UserSpecified FSACTargetRuntime.NET, None, _ ->
                return! monoNotFound ()

            // dotnet SDK handling
            | UserSpecified FSACTargetRuntime.NetcoreFdd, _, Some dotnet ->
                return! spawnNetcoreFSAC dotnet
            | UserSpecified FSACTargetRuntime.NetcoreFdd, _, None ->
                return! dotnetNotFound ()

            // when we infer a runtime then we can suggest to the user our other options
            // .NET framework handling (looks similar to above just with suggestion)
            | Implied FSACTargetRuntime.NET, None, Some _dotnet when Environment.isWin ->
                suggestNetCore() |> ignore
                return! spawnNetFSAC ""
            | Implied FSACTargetRuntime.NET, Some mono, Some _dotnet ->
                suggestNetCore() |> ignore
                return! spawnNetFSAC mono
            | Implied FSACTargetRuntime.NET, None, Some _dotnet ->
                suggestNetCore() |> ignore
                return! monoNotFound ()

            // these case actually never happens right now (see the `targetRuntime` calculation above), but it's here for completeness,
            // IE a scenario in which dotnet isn't found but we have located the proper execution environment for .Net framework
            | Implied FSACTargetRuntime.NetcoreFdd, None, None when Environment.isWin ->
                suggestNet () |> ignore
                return! dotnetNotFound ()
            | Implied FSACTargetRuntime.NetcoreFdd, Some mono, None when not Environment.isWin ->
                suggestNet () |> ignore
                return! dotnetNotFound ()

            | runtime, mono, dotnet ->
                return failwithf "unsupported combination of runtime/mono/dotnet: %O/%O/%O" runtime mono dotnet
        }

    let ensurePrereqsForRuntime runtime =
        match runtime with
        | UserSpecified FSACTargetRuntime.NET | Implied FSACTargetRuntime.NET ->
            promise {
                let! fsc = fsc ()
                let! msbuild = msbuild ()
                match fsc, msbuild with
                | Some _, Some _ -> return ()
                | _, _ ->
                    if Environment.isWin
                    then return! vscode.window.showErrorMessage "Visual Studio Build Tools not found. Please install them from the [Visual Studio Download Page](https://visualstudio.microsoft.com/thank-you-downloading-visual-studio/?sku=BuildTools&rel=15)" |> Promise.map ignore
                    else return! vscode.window.showErrorMessage "Mono installation not found. Please install the latest version for your operating system from the [Mono Project](https://www.mono-project.com/download/stable/)" |> Promise.map ignore
            }
        | UserSpecified FSACTargetRuntime.NetcoreFdd | Implied FSACTargetRuntime.NetcoreFdd ->
            Promise.lift ()

    let startFSAC () =
        let ionidePluginPath = VSCodeExtension.ionidePluginPath ()
        spawnFSACForRuntime targetRuntime ionidePluginPath

    let start () =
        let rec doRetry procPromise =
            procPromise ()
            |> Promise.onSuccess (fun (childProcess: ChildProcess.ChildProcess) ->
                childProcess.on("exit", fun () ->
                    if exitRequested
                    then
                        log.Info("FSAC killed by us")
                        ()
                    else
                        log.Info("FSAC killed by outside event, restarting")
                        doRetry procPromise |> ignore
                ) |> ignore
            )

        let startByDevMode =
            if shouldStartFSAC then
                doRetry startFSAC
            else
                let msg = sprintf "Using FSAC from url %O provided by configuration 'FSharp.fsacUrl'." fsacUrl
                log.Debug(msg)
                vscode.window.showInformationMessage(msg) |> ignore
                Promise.empty

        startByDevMode
        |> Promise.onSuccess (fun _ ->
            socketNotify <- startSocket "notify"
            socketNotifyWorkspace <- startSocket "notifyWorkspace"
            socketNotifyAnalyzer <- startSocket "notifyAnalyzer"
            ()
        )
        |> Promise.bind (fun _ -> ensurePrereqsForRuntime targetRuntime)

    let stop () =
        exitRequested <- true
        service |> Option.iter (fun n -> n.kill "SIGKILL")
        service <- None
        exitRequested <- false
        ()

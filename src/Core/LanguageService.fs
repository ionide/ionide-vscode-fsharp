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
open Ionide.VSCode.Helpers

module LanguageService =
    let ax =  Node.require.Invoke "axios" |>  unbox<Axios.AxiosStatic>

    let devMode = false

    [<RequireQualifiedAccess>]
    type LogConfigSetting = None | Output | DevConsole | Both
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

        let showCurrentLevel level =
            if level <> Level.DEBUG then
                editorSideLogger.Info ("Logging to output at level %s. If you want detailed messages, try level DEBUG.", (level.ToString()))

        editorSideLogger.ChanMinLevel |> showCurrentLevel

        vscode.workspace.onDidChangeConfiguration
        |> Event.invoke (fun () ->
            editorSideLogger.ChanMinLevel <- logLanguageServiceRequestsOutputWindowLevel ()
            editorSideLogger.ChanMinLevel |> showCurrentLevel )
        |> ignore

        // show the stdout data printed from FSAC in a separate channel
        let fsacStdOutWriter =
            if logRequestsToConsole then
                let chan = window.createOutputChannel (channelName + " (server)")
                chan.append
            else
                ignore

        editorSideLogger, fsacStdOutWriter

    let log, fsacStdoutWriter = createConfiguredLoggers "IONIDE-FSAC" "F# Language Service"

    let genPort () =
        let r = JS.Math.random ()
        let r' = r * (8999. - 8100.) + 8100.
        r'.ToString().Substring(0,4)

    let port = if devMode then "8088" else genPort ()
    let private url fsacAction requestId = (sprintf "http://127.0.0.1:%s/%s?requestId=%i" port fsacAction requestId)
    let mutable private service : child_process_types.ChildProcess option =  None
    let mutable private socket : WebSocket option = None
    let private platformPathSeparator = if Process.isMono () then "/" else "\\"
    let private makeRequestId =
        let mutable requestId = 0
        fun () -> (requestId <- requestId + 1); requestId
    let private relativePathForDisplay (path: string) =
        path.Replace(vscode.workspace.rootPath + platformPathSeparator, "~" + platformPathSeparator)
    let private makeOutgoingLogPrefix (requestId:int) = String.Format("REQ ({0:000}) ->", requestId)
    let private makeIncomingLogPrefix (requestId:int) = String.Format("RES ({0:000}) <-", requestId)

    let private logOutgoingRequest requestId (fsacAction:string) obj =
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

    let private logIncomingResponse requestId fsacAction (started: DateTime) (r: Axios.AxiosXHR<_>) (res: _ option) (ex: exn option) =
        let elapsed = DateTime.Now - started
        match res, ex with
        | Some res, None ->
            log.Debug(makeIncomingLogPrefix(requestId) + " {%s} in %s ms: Kind={\"%s\"}\nData=%j", fsacAction, elapsed.TotalMilliseconds, res?Kind, res?Data)
        | None, Some ex ->
            log.Error (makeIncomingLogPrefix(requestId) + " {%s} ERROR in %s ms: {%j}, Data=%j", fsacAction, elapsed.TotalMilliseconds, ex.ToString(), obj)
        | _, _ -> log.Error(makeIncomingLogPrefix(requestId) + " {%s} ERROR in %s ms: %j, %j, %j", fsacAction, elapsed.TotalMilliseconds, res, ex.ToString(), obj)

    let private logIncomingResponseError requestId fsacAction (started: DateTime) (r: obj) =
        let elapsed = DateTime.Now - started
        log.Error (makeIncomingLogPrefix(requestId) + " {%s} ERROR in %s ms: %s Data=%j",
                    fsacAction, elapsed.TotalMilliseconds, r.ToString(), obj)

    type FSACResponse<'b> =
        | Error of string * ErrorData
        | Info of obj
        | Kind of string * 'b
        | Invalid

    let parseError (err: obj) =
        let data =
            match err?Code |> unbox with
            | ErrorCodes.GenericError ->
                ErrorData.GenericError
            | ErrorCodes.ProjectNotRestored ->
                ErrorData.ProjectNotRestored (err?AdditionalData |> unbox)
            | unknown ->
                //todo log not recognized for Debug
                ErrorData.GenericError
        (err?Message |> unbox<string>), data

    let prettyPrintError fsacAction (msg: string) (err: ErrorData) =
        let whenMsg =
            match fsacAction with
            | "project" -> "Project loading failed"
            | a -> sprintf "Cannot execute %s" a
        let d =
            match err with
            | ErrorData.GenericError ->
                ""
            | ErrorData.ProjectNotRestored data ->
                sprintf "'%s'" data.Project
        sprintf "%s, %s %s" whenMsg msg d

    let private requestRaw<'a, 'b> (fsacAction: string) id requestId (obj : 'a) =
        let started = DateTime.Now
        let fullRequestUrl = url fsacAction requestId
        logOutgoingRequest requestId fsacAction obj
        let options =
            createObj [
                "proxy" ==> false
            ]

        ax.post (fullRequestUrl, obj, unbox options)
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

    let private request<'a, 'b> (fsacAction: string) id requestId (obj : 'a) =
         requestRaw fsacAction id requestId obj
         |> Promise.map(fun (r: FSACResponse<'b>) ->
             match r with
             | FSACResponse.Error (msg, err) ->
                log.Error (prettyPrintError fsacAction msg err)
                null |> unbox
             | FSACResponse.Info err -> null |> unbox
             | FSACResponse.Kind (t, res) -> res
             | FSACResponse.Invalid -> null |> unbox
          )

    let private handleUntitled (fn : string) = if fn.EndsWith ".fs" || fn.EndsWith ".fsx" then fn else (fn + ".fsx")

    let project s =
        {ProjectRequest.FileName = s}
        |> request "project" 0 (makeRequestId())

    let parseProjects s =
        {ProjectRequest.FileName = s}
        |> request "parseProjects" 0 (makeRequestId())

    let parseProjectsInBackground s =
        {ProjectRequest.FileName = s}
        |> request "parseProjectsInBackground" 0 (makeRequestId())

    let parse path (text : string) (version : float) =
        let lines = text.Replace("\uFEFF", "").Split('\n')
        { ParseRequest.FileName = handleUntitled path
          ParseRequest.Lines = lines
          ParseRequest.IsAsync = true
          ParseRequest.Version = int version }
        |> request "parse" 0 (makeRequestId())

    let helptext s =
        {HelptextRequest.Symbol = s}
        |> request "helptext" 0 (makeRequestId())

    let completion fn sl line col keywords =
        {CompletionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "Contains"; SourceLine = sl; IncludeKeywords = keywords}
        |> request "completion" 1 (makeRequestId())

    let symbolUse fn line col =
        {PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = ""}
        |> request "symboluse" 0 (makeRequestId())

    let symbolUseProject fn line col =
        {PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = ""}
        |> request "symboluseproject" 0 (makeRequestId())

    let methods fn line col =
        {PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = ""}
        |> request "methods" 0 (makeRequestId())

    let tooltip fn line col =
        {PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = ""}
        |> request "tooltip" 0 (makeRequestId())

    let toolbar fn line col =
        {PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = ""}
        |> request "tooltip" 0 (makeRequestId())

    let signature fn line col =
        {PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = ""}
        |> request "signature" 0 (makeRequestId())

    let findDeclaration fn line col =
        {PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = ""}
        |> request "finddeclaration" 0 (makeRequestId())

    let f1Help fn line col =
        {PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = ""}
        |> request "help" 0 (makeRequestId())

    let signatureData fn line col =
        {PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = ""}
        |> request "signatureData" 0 (makeRequestId())

    let declarations fn version=
        {DeclarationsRequest.FileName = handleUntitled fn; Version = version}
        |> request "declarations" 0 (makeRequestId())


    let declarationsProjects () =
        "" |> request "declarationsProjects" 0 (makeRequestId())

    let compilerLocation () =
        "" |> request "compilerlocation" 0 (makeRequestId())

    let lint s =
        {ProjectRequest.FileName = s}
        |> request "lint" 0 (makeRequestId())

    let resolveNamespaces fn line col =
        {PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = ""}
        |> request "namespaces" 0 (makeRequestId())

    let unionCaseGenerator fn line col =
        {PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = ""}
        |> request "unionCaseGenerator" 0 (makeRequestId())


    let registerNotify (cb : 'a [] -> unit) =
        socket |> Option.iter (fun ws ->
            ws.on_message((fun (res : string) ->
                res
                |> ofJson
                |> Seq.map ofJson
                |> Seq.where (fun n -> unbox n?Kind <>  "info" && unbox n?Kind <> "error")
                |> Seq.toArray
                |> cb
                ) |> unbox) |> ignore
            ())

    let startSocket () =
        let address = sprintf "ws://localhost:%s/notify" port
        try
            let sck = WebSocket address
            socket <- Some sck
        with
        | e ->
            socket <- None
            log.Error("Initializing notify error: %s", e.Message)


    let start' path =
        Promise.create (fun resolve reject ->
            let child =
                if Process.isMono () then
                    let mono = "FSharp.monoPath" |> Configuration.get "mono"
                    child_process.spawn(mono, [| path; string port|] |> ResizeArray)
                else
                    child_process.spawn(path, [| string port|] |> ResizeArray)

            let mutable isResolvedAsStarted = false
            child
            |> Process.onOutput (fun n ->
                // The `n` object is { "type":"Buffer", "data":[...bytes] }
                // and by calling .ToString() we are decoding the buffer into a string.
                let outputString = n.ToString()
                // Wait until FsAC sends the 'listener started' magic string until
                // we inform the caller that it's ready to accept requests.
                let isStartedMessage = outputString.Contains "listener started in"
                if isStartedMessage then
                    fsacStdoutWriter ("Resolving startup promise because FSAC printed the 'listener started' message")
                    fsacStdoutWriter "\n"
                    service <- Some child
                    resolve child
                    isResolvedAsStarted <- true

                fsacStdoutWriter outputString
            )
            |> Process.onError (fun e ->
                fsacStdoutWriter (e.ToString())
                if not isResolvedAsStarted then
                    reject (e.ToString())
            )
            |> Process.onErrorOutput (fun n ->
                fsacStdoutWriter (n.ToString())
                if not isResolvedAsStarted then
                    reject (n.ToString())
            )
            |> ignore
        )
        //startSocket ()

        |> Promise.onFail (fun err ->
            log.Error("Failed to start language services. %s", err)
            if Process.isMono () then
                "Failed to start language services. Please check if mono is in PATH"
            else
                "Failed to start language services. Please check if Microsoft Build Tools 2013 are installed"
            |> vscode.window.showErrorMessage
            |> ignore)


    let start () =
         let path =
            try
                (VSCode.getPluginPath "Ionide.ionide-fsharp") + "/bin/FsAutoComplete.Suave.exe"
            with
            | _ -> (VSCode.getPluginPath "Ionide.Ionide-fsharp") + "/bin/FsAutoComplete.Suave.exe"

         if devMode then Promise.empty else start' path

    let stop () =
        service |> Option.iter (fun n -> n.kill "SIGKILL")
        service <- None
        ()

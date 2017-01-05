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

    [<RequireQualifiedAccess>]
    type LogConfigSetting = None | Output | DevConsole | Both
    let logLanguageServiceRequestsConfigSetting =
        try
            match "FSharp.logLanguageServiceRequests" |> Configuration.get "" with
            | "devconsole" -> LogConfigSetting.DevConsole
            | "output" -> LogConfigSetting.Output
            | "both" -> LogConfigSetting.Both
            | _ -> LogConfigSetting.None
        with
        | _ -> LogConfigSetting.None

    let logLanguageServiceRequestsOutputWindowLevel =
        try
            match "FSharp.logLanguageServiceRequestsOutputWindowLevel" |> Configuration.get "INFO" with
            | "DEBUG" -> Level.DEBUG
            | "INFO" -> Level.INFO
            | "WARN" -> Level.WARN
            | "ERROR" -> Level.ERROR
            | _ -> Level.INFO
        with
        | _ -> Level.INFO

    // Always log to the logger, and let it decide where/if to write the message
    let log =
        let channel, logRequestsToConsole =
            match logLanguageServiceRequestsConfigSetting with
            | LogConfigSetting.None -> None, false
            | LogConfigSetting.Both -> Some (window.createOutputChannel "F# Language Service"), true
            | LogConfigSetting.DevConsole -> None, true
            | LogConfigSetting.Output -> Some (window.createOutputChannel "F# Language Service"), false

        let consoleMinLevel = if logRequestsToConsole then DEBUG else WARN
        let inst = ConsoleAndOutputChannelLogger(Some "IONIDE-FSAC", logLanguageServiceRequestsOutputWindowLevel, channel, Some consoleMinLevel)
        if logLanguageServiceRequestsOutputWindowLevel <> Level.DEBUG then
            let levelString = logLanguageServiceRequestsOutputWindowLevel.ToString()
            inst.Info ("Logging to output at level %s. If you want detailed messages, try level DEBUG.", levelString)
        inst

    let genPort () =
        let r = JS.Math.random ()
        let r' = r * (8999. - 8100.) + 8100.
        r'.ToString().Substring(0,4)

    let port = genPort ()
    let private url fsacAction = (sprintf "http://127.0.0.1:%s/%s" port fsacAction)
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
        | None, None -> log.Info (makeOutgoingLogPrefix(requestId) + " {%s}", fsacAction)
        | Some extraTmpl, Some extraArg -> log.Info (makeOutgoingLogPrefix(requestId) + " {%s}" + extraTmpl, fsacAction, extraArg)
        | _, _ -> failwithf "cannot happen %A" extraPropInfo

    let private logIncomingResponse requestId fsacAction (started: DateTime) (r: Axios.AxiosXHR<_>) (res: _ option) (ex: exn option) =
        let elapsed = DateTime.Now - started
        match res, ex with
        | Some res, None ->
            let debugLog : string*obj[] = makeIncomingLogPrefix(requestId) + " {%s} in %s ms: Kind={\"%s\"}\nData=%j",
                                          [| fsacAction; elapsed.TotalMilliseconds; res?Kind; res?Data |]
            let infoLog : string*obj[] = makeIncomingLogPrefix(requestId) + " {%s} in %s ms: Kind={\"%s\"} ",
                                          [| fsacAction; elapsed.TotalMilliseconds; res?Kind |]
            log.DebugOrInfo debugLog infoLog
        | None, Some ex ->
            log.Error (makeIncomingLogPrefix(requestId) + " {%s} ERROR in %s ms: {%j}, Data=%j", fsacAction, elapsed.TotalMilliseconds, ex.ToString(), obj)
        | _, _ -> log.Error(makeIncomingLogPrefix(requestId) + " {%s} ERROR in %s ms: %j, %j, %j", fsacAction, elapsed.TotalMilliseconds, res, ex.ToString(), obj)

    let private logIncomingResponseError requestId fsacAction (started: DateTime) (r: obj) =
        let elapsed = DateTime.Now - started
        log.Error (makeIncomingLogPrefix(requestId) + " {%s} ERROR in %s ms: %s Data=%j",
                    fsacAction, elapsed.TotalMilliseconds, r.ToString(), obj)

    let private request<'a, 'b> (fsacAction: string) id requestId (obj : 'a) =
        let started = DateTime.Now
        let fullRequestUrl = url fsacAction
        logOutgoingRequest requestId fsacAction obj

        ax.post (fullRequestUrl, obj)
        |> Promise.onFail (fun r ->
            // The outgoing request was not made
            logIncomingResponseError requestId fsacAction started r
            null |> unbox
        )
        |> Promise.map(fun r ->
            // the outgoing request was made
            try
                let res = (r.data |> unbox<string[]>).[id] |> JS.JSON.parse |> unbox<'b>
                logIncomingResponse requestId fsacAction started r (Some res) None
                if res?Kind |> unbox = "error" || res?Kind |> unbox = "info" then null |> unbox
                else res
            with
            | ex ->
                logIncomingResponse requestId fsacAction started r None (Some ex)
                null |> unbox
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

    let declarations fn =
        {DeclarationsRequest.FileName = handleUntitled fn}
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
                let isStartedMessage = outputString.Contains ": listener started in"
                if isStartedMessage then
                    log.Debug ("got FSAC line, is it the started message? %s", isStartedMessage)
                    service <- Some child
                    resolve child
                    isResolvedAsStarted <- true

                // always log the output
                log.Debug ("FSAC stdout: %s", n.ToString())
            )
            |> Process.onError (fun e ->
                log.Error ("FSAC process error: %s", e.ToString())
                if not isResolvedAsStarted then
                    reject ()
            )
            |> Process.onErrorOutput (fun n ->
                log.Error ("FSAC stderr: %s", n.ToString())
                if not isResolvedAsStarted then
                    reject ()
            )
            |> ignore
        )
        |> Promise.onSuccess (fun _ ->
            () //startSocket ()
        )
        |> Promise.onFail (fun _ ->
            if Process.isMono () then
                "Failed to start language services. Please check if mono is in PATH"
            else
                "Failed to start language services. Please check if Microsoft Build Tools 2013 are installed"
            |> vscode.window.showErrorMessage
            |> ignore)



    let start () =
         let path = (VSCode.getPluginPath "Ionide.Ionide-fsharp") + "/bin/FsAutoComplete.Suave.exe"
         start' path

    let stop () =
        service |> Option.iter (fun n -> n.kill "SIGKILL")
        service <- None
        ()
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

module LanguageService =
    let ax =  Node.require.Invoke "axios" |>  unbox<Axios.AxiosStatic>

    let logRequestsToConsole =
        try
            workspace.getConfiguration().get("FSharp.logLanguageServiceRequestsToConsole", false)
        with
        | _ -> false

    let logRequestsToOutputWindow =
        try
            workspace.getConfiguration().get("FSharp.logLanguageServiceRequestsToOutputWindow", false)
        with
        | _ -> false

    // Always log to the logger, and let it decide where/if to write the message
    let log =
        let channel = if logRequestsToOutputWindow then Some (window.createOutputChannel "F# Language Service") else None
        ConsoleAndOutputChannelLogger(Some "IONIDE-FSAC", INF, channel, logRequestsToConsole)

    let genPort () =
        let r = JS.Math.random ()
        let r' = r * (8999. - 8100.) + 8100.
        r'.ToString().Substring(0,4)

    let port = genPort ()
    let private url s = sprintf @"http://localhost:%s/%s" port s

    let mutable private service : child_process_types.ChildProcess option =  None

    let request<'a, 'b> (ep: string) id  (obj : 'a) =       
        log.Debug ("REQUEST  : %s, Request=%j", ep, obj)

        // At the INFO level, it's nice to see only the key data to get an overview of
        // what's happening, without being bombarded with too much detail
        if JS.isDefined (obj?FileName) then
            log.Info ("REQUEST  : %s, FileName=%j", ep, obj?FileName)
        else if JS.isDefined (obj?Symbol) then
            log.Info ("REQUEST  : %s, Symbol=%j", ep, obj?Symbol)
        else
            log.Info ("REQUEST  : %s", ep)

        ax.post (ep, obj)
        |> Promise.onFail (fun r ->
            log.Error ("FAILED   : %s, Failure=%j Data=%j", ep, r.ToString(), obj)
            null |> unbox
        )
        |> Promise.map(fun r ->
            try
                let res = (r.data |> unbox<string[]>).[id] |> JS.JSON.parse |> unbox<'b>
                log.Info ("RESPONSE : %s, Kind=%s", ep, res?Kind)
                log.Debug ("RESPONSE : %s, Kind=%s, Data=%j", ep, res?Kind, res?Data)
                if res?Kind |> unbox = "error" || res?Kind |> unbox = "info" then null |> unbox
                else res
            with
            | ex ->
                log.Error ("RESPONSE : %s, Failure=%j, Data=%j", ep, ex.ToString(), obj)
                null |> unbox
        )

    let project s =
        {ProjectRequest.FileName = s}
        |> request (url "project") 0

    let parseProject () =
        ""
        |> request (url "parseProjects") 0

    let parse path (text : string) =
        let lines = text.Replace("\uFEFF", "").Split('\n')
        {ParseRequest.FileName = path; ParseRequest.Lines = lines; ParseRequest.IsAsync = true }
        |> request (url "parse") 0

    let helptext s =
        {HelptextRequest.Symbol = s}
        |> request (url "helptext") 0

    let completion fn sl line col =
        {CompletionRequest.Line = line; FileName = fn; Column = col; Filter = "Contains"; SourceLine = sl}
        |> request (url "completion")1

    let symbolUse fn line col =
        {PositionRequest.Line = line; FileName = fn; Column = col; Filter = ""}
        |> request (url "symboluse") 0

    let symbolUseProject fn line col =
        {PositionRequest.Line = line; FileName = fn; Column = col; Filter = ""}
        |> request (url "symboluseproject") 0

    let methods fn line col =
        {PositionRequest.Line = line; FileName = fn; Column = col; Filter = ""}
        |> request (url "methods") 0

    let tooltip fn line col =
        {PositionRequest.Line = line; FileName = fn; Column = col; Filter = ""}
        |> request (url "tooltip") 0

    let toolbar fn line col =
        {PositionRequest.Line = line; FileName = fn; Column = col; Filter = ""}
        |> request (url "tooltip") 0

    let findDeclaration fn line col =
        {PositionRequest.Line = line; FileName = fn; Column = col; Filter = ""}
        |> request (url "finddeclaration") 0

    let declarations fn =
        {DeclarationsRequest.FileName = fn}
        |> request (url "declarations") 0


    let declarationsProjects () =
        "" |> request (url "declarationsProjects")  0

    let compilerLocation () =
        "" |> request (url "compilerlocation")  0

    let start' path =
        Promise.create (fun resolve reject ->
            let child =
                if Process.isMono () then
                    let mono = workspace.getConfiguration().get("FSharp.monoPath", "mono")
                    child_process.spawn(mono, [| path; string port|] |> ResizeArray)
                else
                    child_process.spawn(path, [| string port|] |> ResizeArray)

            child
            |> Process.onOutput (fun n ->
                // Wait until FsAC sends the 'listener started' magic string until
                // we inform the caller that it's ready to accept requests.
                let isStartedMessage = (n.ToString().Contains(": listener started in"))
<<<<<<< 1898be6147b8ca75c1b5fbc4515c1937f7b3325b
                if isStartedMessage then
                    Browser.console.log ("[IONIDE-FSAC-SIG] started message?", isStartedMessage)
                    service <- Some child
=======
                if isStartedMessage then 
                    log.Debug ("got FSAC line, is it the started message? %s", isStartedMessage)
                    service <- Some child 
>>>>>>> Use logging API in LanguageService
                    resolve child
                else
                   log.Debug ("got FSAC line: %j", n)
            )
<<<<<<< 1898be6147b8ca75c1b5fbc4515c1937f7b3325b
            |> Process.onErrorOutput (fun n ->
                Browser.console.error (n.ToString())
=======
            |> Process.onErrorOutput (fun n -> 
                log.Error ("got FSAC error output: %j", n)
>>>>>>> Use logging API in LanguageService
                reject ()
            )
            |> Process.onError (fun e ->
                log.Error ("got FSAC error: %j", e)
                reject ()
            )
            |> ignore
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

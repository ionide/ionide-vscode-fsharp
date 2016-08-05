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

    let logRequests =
        workspace.getConfiguration().get("FSharp.logLanguageServiceRequestsToConsole", false)

    let genPort () =
        let r = JS.Math.random ()
        let r' = r * (8999. - 8100.) + 8100.
        r'.ToString().Substring(0,4)

    let port = genPort ()
    let private url s = sprintf @"http://localhost:%s/%s" port s

    let mutable private service : child_process_types.ChildProcess option =  None

    let request<'a, 'b> ep id  (obj : 'a) =
        if logRequests then Browser.console.log ("[IONIDE-FSAC-REQ]", id, ep, obj)
        ax.post (ep, obj)
        |> Promise.success(fun r ->
            try
                let res = (r.data |> unbox<string[]>).[id] |> JS.JSON.parse |> unbox<'b>
                if logRequests then
                    match res?Kind |> unbox with
                    | "error" -> Browser.console.error ("[IONIDE-FSAC-RES]", id, ep, res?Kind, res?Data)
                    | _ -> Browser.console.info ("[IONIDE-FSAC-RES]", id, ep, res?Kind, res?Data)
                if res?Kind |> unbox = "error" || res?Kind |> unbox = "info" then null |> unbox
                else res
            with
            | ex ->
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

    let start () =
        promise {
            let path = (VSCode.getPluginPath "Ionide.Ionide-fsharp") + "/bin/FsAutoComplete.Suave.exe"
            let child = if Process.isMono () then child_process.spawn("mono", [| path; string port|] |> ResizeArray) else child_process.spawn(path, [| string port|] |> ResizeArray)
            service <- Some child
            child.stderr?on $ ("data", fun n -> Browser.console.error (n.ToString())) |> ignore
            child.stdout?on $ ("data", fun n -> Browser.console.log (n.ToString())) |> ignore
        }
        |> Promise.fail (fun x ->
            if Process.isMono () then
                "Failed to start language services. Please check if mono is in PATH"
            else
                "Failed to start language services. Please check if Microsoft Build Tools 2013 are installed"
            |> vscode.window.showErrorMessage
            |> ignore
        )

    let stop () =
        service |> Option.iter (fun n -> n.kill "SIGKILL")
        service <- None
        ()

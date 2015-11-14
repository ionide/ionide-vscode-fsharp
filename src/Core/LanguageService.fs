namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.fs
open FunScript.TypeScript.child_process

open DTO
open Ionide.VSCode.Helpers

[<ReflectedDefinition>]
module LanguageService =
    let genPort () =
        let r = Globals.Math.random ()
        let r' = r * (8999. - 8100.) + 8100.
        r'.ToString().Substring(0,4)

    let port = genPort ()
    let private url s = sprintf @"http://localhost:%s/%s" port s

    let mutable private service : ChildProcess option =  None

    let private request<'T> (url : string) (data: 'T)  = async {
        let r = System.Net.WebRequest.Create url
        let req: FunScript.Core.Web.WebRequest = unbox r
        req.Headers.Add("Accept", "application/json")
        req.Headers.Add("Content-Type", "application/json")
        req.Method <- "POST"

        let str = Globals.JSON.stringify data
        let data = System.Text.Encoding.UTF8.GetBytes str
        let stream = req.GetRequestStream()
        stream.Write (data, 0, data.Length )
        let! res = req.AsyncGetResponse ()
        let stream =  res.GetResponseStream()
        let data = System.Text.Encoding.UTF8.GetString stream.Contents
        let d = Globals.JSON.parse data
        let res = unbox<string[]>(d)
        return res
    }

    let private parseResponse s =
        Globals.JSON.parse s |> unbox<_>

    let private send i (req : Async<string []>)  =
        Globals.Promise.Create(fun (resolve : Func<Result<'T>,_>) (error : Func<obj,_>) ->
            async {
                let! r = req
                r |> Array.iter Globals.console.log
                let result = r.[i] |> parseResponse
                if result.Kind = "error" || result.Kind = "info" then error.Invoke (result.Kind |> unbox<string>)
                else resolve.Invoke result
            } |> Async.StartImmediate
        )

    let project s =
        {ProjectRequest.FileName = s}
        |> request (url "project")
        |> send 0

    let parse path (text : string) =
        let lines = text.Replace("\uFEFF", "").Split('\n')
        {ParseRequest.FileName = path; ParseRequest.Lines = lines; ParseRequest.IsAsync = true }
        |> request (url "parse")
        |> send 0

    let helptext s =
        {HelptextRequest.Symbol = s}
        |> request (url "helptext")
        |> send 0

    let completion fn line col =
        {PositionRequest.Line = line; FileName = fn; Column = col; Filter = ""}
        |> request (url "completion")
        |> send 1

    let symbolUse fn line col =
        {PositionRequest.Line = line; FileName = fn; Column = col; Filter = ""}
        |> request (url "symboluse")
        |> send 0

    let methods fn line col =
        {PositionRequest.Line = line; FileName = fn; Column = col; Filter = ""}
        |> request (url "methods")
        |> send 0

    let tooltip fn line col =
        {PositionRequest.Line = line; FileName = fn; Column = col; Filter = ""}
        |> request (url "tooltip")
        |> send 0

    let toolbar fn line col =
        {PositionRequest.Line = line; FileName = fn; Column = col; Filter = ""}
        |> request (url "tooltip")
        |> send 0

    let findDeclaration fn line col =
        {PositionRequest.Line = line; FileName = fn; Column = col; Filter = ""}
        |> request (url "finddeclaration")
        |> send 0

    let declarations fn =
        {DeclarationsRequest.FileName = fn}
        |> request (url "declarations")
        |> send 0

    let compilerLocation () =
        "" |> request (url "compilerlocation") |> send 0

    let start () =
        let path = (VSCode.getPluginPath "Ionide.ionide-fsharp") + "/bin/fsautocomplete.suave.exe"
        let child = if Process.isMono () then Globals.spawn("mono", [| path; string port|]) else Globals.spawn(path, [| string port|])
        service <- Some child
        child.stderr.on("data", unbox<Function>( fun n -> Globals.console.error (n.ToString()))) |> ignore
        child.stdout.on("data", unbox<Function>( fun n -> Globals.console.log (n.ToString()))) |> ignore
        ()

    let stop () =
        service |> Option.iter (fun n -> n.kill "SIGKILL")
        service <- None
        ()

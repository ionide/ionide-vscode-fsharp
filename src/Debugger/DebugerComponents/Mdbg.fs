module Mdbg

open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.Node
open Fable.Import.Node.child_process_types
open Helpers

type Thread = {
    id: float
    name : string
}

type StackFrame = {
    id : float
    name : string
    source : string option
    line : float
}

type Variable = {
    name: string
    value: string
}

type Continue =
| Terminated
| Breakpoint of threadId : float
| Exception of threadId : float

type BreakpointStatus =
| Bound
| Unbound

type Breakpoint = {
    id : float
    line : float
    file : string
    status : BreakpointStatus
}


let mutable private debugProcess : ChildProcess option = None
let mutable private resolve : (string -> unit) option = Some ignore
let mutable private reject : (string -> unit) option = Some ignore
let mutable private answer = ""
let mutable private busy = false


let spawn dir =
    let mdbgPath = path.join(dir, "bin_mdbg", "mdbg.exe")
    let proc =
        Process.spawn mdbgPath
        |> Process.onOutput (fun n ->
            resolve |> Option.iter (fun res ->
                let output = n.ToString()
                answer <- answer + output
                if answer.Contains "mdbg>" then
                    log ("{ ANSWER }\n" + answer)
                    res answer
                    resolve <- None
                    reject <- None
                    answer <- ""
                    busy <-false ))
        |> Process.onErrorOutput (fun n ->
            reject |> Option.iter (fun rej ->
                log ("{ ERROR }\n" + answer)
                let output = n.ToString()
                rej answer
                resolve <- None
                reject <- None
                answer <- ""
                busy <-false ))
    debugProcess <- Some proc
    busy <- true

let private delay ms =
    Promise.create(fun res rej -> setTimeout(res, ms) |> ignore )

let rec private send (cmd : string) =

    if not busy then
        match debugProcess with
        | None -> Promise.reject "Mdbg not started"
        | Some dp ->
            log ("{ REQ SEND } " + cmd)
            busy <- true
            dp.stdin?write $ (cmd + "\n")
            Promise.create (fun res rej ->
                resolve <- Some res
                reject <- Some rej
            )
    else
            log ("{ REQ WAITING } " + cmd)
            delay 100
            |> Promise.bind (fun _ -> send cmd)

let start path =
    path
    |> sprintf "r %s"
    |> send
    |> Promise.map (ignore)

let go () =
    send "go"
    |> Promise.map (fun res ->
        if res.Contains "STOP: Process Exited" then
            Terminated
        elif res.Contains "STOP: Breakpoint" then
            Breakpoint 0. //TODO: Find Thread
        else //TODO
            Exception 0. //TODO: Find Thread

    )


let next () =
    send "n"
    |> Promise.map (fun _ -> 0.) //TODO: Find Thread

let stepIn () =
    send "s"
    |> Promise.map (fun _ -> 0.) //TODO: Find Thread

let stepOut () =
    send "u"
    |> Promise.map (fun _ -> 0.) //TODO: Find Thread

let setBreakpoint file line =
    sprintf "b %s:%d" file line
    |> send
    |> Promise.map (fun res ->
        let ln = res.Split('\n').[0]
        let x = ln.Split('#')
        let id = x.[1].Split(' ').[0] |> float
        let state =
            if ln.Contains "unbound" then Unbound else Bound
        {
            Breakpoint.id = id
            line = unbox line
            file = file
            status = state
        }
    )


let deleteBreakpoint id =
    sprintf "del %d" id
    |> send

let getThreads () =
    let parseThread (line : string) =
        try
            let thread = line.Split('(')
            let name = thread.[0].Trim()
            let id = name.Split(':').[1] |> float
            Some {Thread.name = name; id = id}
        with
        | _ -> None

    send "t"
    |> Promise.map (fun res ->
        res.Split('\n')
        |> Array.map (fun s -> s.Trim())
        |> Array.where(fun s -> s.StartsWith "th")
        |> Array.choose (parseThread) )


let getStack depth thread =
    let parseStackFrame (line : string) =
        try
            let ns = line.Split([|". "|], System.StringSplitOptions.RemoveEmptyEntries)
            let id = ns.[0].Replace("*", "") |> float
            let xs = ns.[1].Split('(')
            let name = xs.[0].Trim()
            let location = xs.[1].Replace(")", "")

            let source, line =
                if location = "source line information unavailable" then
                    None, 0.
                else
                    let getExtension (lc : string) =
                        if lc.Contains ".fs" then ".fs"
                        elif lc.Contains ".fsx" then ".fsx"
                        elif lc.Contains ".cs" then ".cs"
                        else ""

                    let locs = location.Split([| ".fs:"; ".cs:"; ".fsx:" |], System.StringSplitOptions.RemoveEmptyEntries )
                    let p = locs.[0] + getExtension location
                    let line = locs.[1] |> float
                    Some p, line
            Some {
                StackFrame.name = name
                id = id
                source = source
                line = line
            }
        with
        | _ -> None

    sprintf "w -c %d %d" depth thread
    |> send
    |> Promise.map (fun res ->
        res.Split('\n')
        |> Array.map (fun s -> s.Trim())
        |> Array.where(fun s -> not (s.Contains "mdbg>"))
        |> Array.choose (parseStackFrame)

    )

let getVariables () =
    let parseVariable (line : string) =
        let ls = line.Split('=')
        {Variable.name = ls.[0]; value = ls.[1] }

    send "p"
    |> Promise.map (fun res ->
        res.Split('\n')
        |> Array.map (fun s -> s.Trim())
        |> Array.where(fun s -> not (s.Contains "mdbg"))
        |> Array.map parseVariable)


let getVariable item =
    sprintf "p %s" item
    |> send
    |> Promise.map (fun res -> res.Split('\n').[0])

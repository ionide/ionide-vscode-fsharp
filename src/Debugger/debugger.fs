[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Ionide.VSCode.Debugger

open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.DebugSession
open Fable.Import.Node
open Fable.Import.Node.child_process_types

module Process =
    let spawn path =
        child_process.spawn(path)

    let onExit (f : obj -> _) (proc : child_process_types.ChildProcess) =
        proc.on("exit", f |> unbox) |> ignore
        proc

    let onOutput (f : obj -> _) (proc : child_process_types.ChildProcess) =
        proc.stdout?on $ ("data", f |> unbox) |> ignore
        proc

    let onErrorOutput (f : obj -> _) (proc : child_process_types.ChildProcess) =
        proc.stderr?on $ ("data", f |> unbox) |> ignore
        proc

    let onError (f: obj -> _) (proc : child_process_types.ChildProcess) =
        proc?on $ ("error", f |> unbox) |> ignore
        proc


module Mdbg =

    let mutable private debugProcess : ChildProcess option = None
    let mutable private resolve : (string -> unit) option = None
    let mutable private reject : (string -> unit) option = None
    let mutable private answer = ""
    let mutable private busy = false

    let spawn =
        let mdbgPath = path.join("bin_mdbg", "mdbg.exe")
        Process.spawn mdbgPath
        |> Process.onOutput (fun n ->
            resolve |> Option.iter (fun res ->
                let output = n.ToString()
                answer <- answer + output
                if answer.Contains "mdbg>" then

                    res answer
                    resolve <- None
                    answer <- ""
                    busy <-false ))


type IonideDebugger ()  =
    inherit DebugSession()

    member x.initializeRequest(response: DebugProtocol.InitializeResponse, args: DebugProtocol.InitializeRequestArguments) =
        x.sendResponse(response)

    member x.launchRequest(response: DebugProtocol.LaunchResponse, args: DebugProtocol.LaunchRequestArguments): unit =
        let t = 1
        let y = t + 1
        x.sendResponse(response)
        ()

let start () =
    DebugSession.run $ IonideDebugger
    ()

start ()
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Ionide.VSCode.Debugger

open Fable.Core
open Fable.Import.JS
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.DebugSession
open FSharp
open Helpers
open System.Collections.Generic


type LaunchRequestArguments =
    inherit DebugProtocol.LaunchRequestArguments

    abstract member program : string with get,set
    abstract member stopOnEntry : bool option with get,set

type IonideDebugger ()  =
    inherit DebugSession()

    // member private x.breakpoints = Dictionary<


    member x.initializeRequest(response: DebugProtocol.InitializeResponse, args: DebugProtocol.InitializeRequestArguments) =
        log "{LOG} Init called"

        Mdbg.spawn ()
        response.body.supportsEvaluateForHovers <- Some true


        x.sendResponse(response)

    member x.launchRequest(response: DebugProtocol.LaunchResponse, args: LaunchRequestArguments): unit =
        promise {
            log "{LOG} Launch called"
            let! _ = Mdbg.start(args.program)
            if (defaultArg args.stopOnEntry false)  then
                x.sendResponse(response)
                x.sendEvent(unbox (StoppedEvent("breakpoint",0.)))
            else
                let args = createEmpty<DebugProtocol.ContinueArguments>
                args.threadId <- 0.
                x.continueRequest(unbox response,args)
        } |> ignore


    member x.threadsRequest(response: DebugProtocol.ThreadsResponse) =
        promise {
            log "{LOG} Threads called"
            let! threads = Mdbg.getThreads ()

            let body =
                createObj [
                    "threads" ==> (threads |> Collections.Array.map (fun t -> Thread(t.id, t.name)))
                ]

            response.body <- body

            x.sendResponse(response)
        } |> ignore

    member x.stackTraceRequest(response: DebugProtocol.StackTraceResponse, args: DebugProtocol.StackTraceArguments) =
        promise {
            log "{LOG} StackTrace called"
            let! frames = Mdbg.getStack (unbox args.levels) (unbox args.threadId)
            let body =
                createObj [
                    "stackFrames" ==> (frames |> Collections.Array.map (fun t ->
                                                let source = t.source |> Option.map (fun src ->
                                                    let name = Node.path.basename src
                                                    Source(name, src))


                                                StackFrame(t.id, t.name, unbox source, t.line)))
                    "totalFrames" ==> frames.Length //TODO: Not sure what goes here
                ]

            response.body <- body

            x.sendResponse(response)
        } |> ignore

    member x.scopesRequest(response: DebugProtocol.ScopesResponse, args: DebugProtocol.ScopesArguments) =
        //TODO: What the hell should go here?
        promise {
            log "{LOG} Scopes called"
            let body =
                createObj [
                    "scopes" ==> [| Scope("Local",1.,false) |]
                ]
            response.body <- body

            x.sendResponse(response)
        } |> ignore

    member x.variablesRequest(response: DebugProtocol.VariablesResponse, args: DebugProtocol.VariablesArguments) =
        promise {
            log "{LOG} Variables called"
            let! vars = Mdbg.getVariables ()
            let body =
                createObj [
                    "variables" ==> (vars |> Collections.Array.map (fun t -> Variable(t.name, t.value, 0.)))
                ]
            x.sendResponse(response)
        } |> ignore

    member x.continueRequest(response: DebugProtocol.ContinueResponse, args: DebugProtocol.ContinueArguments) =
        promise {
            log "{LOG} Continue called"
            let! status = Mdbg.go ()
            x.sendResponse(response)
            match status with
            | Mdbg.Terminated ->
                x.sendEvent (unbox (TerminatedEvent()))
            | Mdbg.Breakpoint thread  ->
                x.sendEvent (unbox (StoppedEvent("breakpoint",thread)))
            | Mdbg.Exception thread  ->
                x.sendEvent (unbox (StoppedEvent("exception",thread)))
        } |> ignore

    member x.nextRequest(response: DebugProtocol.NextResponse, args: DebugProtocol.NextArguments) =
        promise {
            log "{LOG} Next called"
            let! thread = Mdbg.next ()
            x.sendResponse(response)
            x.sendEvent (unbox (StoppedEvent("step",thread)))
        } |> ignore

    member x.stepInRequest(response: DebugProtocol.StepInResponse) =
        promise {
            log "{LOG} In called"
            let! thread = Mdbg.stepIn ()
            x.sendResponse(response)
            x.sendEvent (unbox (StoppedEvent("step",thread)))
        } |> ignore

    member x.stepOutRequest(response: DebugProtocol.StepOutResponse) =
        promise {
            log "{LOG} Out called"
            let! thread = Mdbg.stepOut ()
            x.sendResponse(response)
            x.sendEvent (unbox (StoppedEvent("step",thread)))
        } |> ignore

    member x.evaluateRequest(response: DebugProtocol.EvaluateResponse, args: DebugProtocol.EvaluateArguments) =
        promise {
            log "{LOG} Evaluate called"
            let! var = Mdbg.getVariable args.expression
            let body =
                createObj [
                    "result" ==> var
                    "variablesReference" ==> 0
                ]
            response.body <- body
            x.sendResponse(response)

        } |> ignore

let start () =
    DebugSession.run $ IonideDebugger
    ()

start ()
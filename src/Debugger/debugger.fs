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

type IonideDebugger () as x =
    inherit DebugSession()

    do
        x.setDebuggerColumnsStartAt1(true)
        x.setDebuggerLinesStartAt1(true)

    member private x.brks = Dictionary<string, Mdbg.Breakpoint []>()


    member x.initializeRequest(response: DebugProtocol.InitializeResponse, args: DebugProtocol.InitializeRequestArguments) =
        log "{LOG} Init called"
        Mdbg.spawn (Node.__dirname)
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
            response.body <- body
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

    //TODO: Breakpoints not working?????
    member x.setBreakPointsRequest(response: DebugProtocol.SetBreakpointsResponse, args: DebugProtocol.SetBreakpointsArguments) =
        promise {
            log "{LOG} Breakpoints called"
            let fileName = args.source.path.Value
            let file = Node.path.basename(fileName)
            let current = if x.brks.ContainsKey fileName then x.brks.[fileName] else [||]

            let toDelete, rest =
                current |> Seq.toList |> List.partition (fun bp -> args.breakpoints.Value |> Seq.exists (fun b -> b.line = bp.line) )

            let! dels  =
                toDelete
                |> Seq.map (fun bp -> Mdbg.deleteBreakpoint (unbox bp.id ))
                |> Helpers.Promise.all

            let toAdd =
                args.breakpoints.Value
                |> Seq.where (fun bp -> current |> Seq.exists (fun b -> b.line = bp.line) |> not)

            let! added =
                toAdd
                |> Seq.map (fun bp ->Mdbg.setBreakpoint file (unbox bp.line) )
                |> Helpers.Promise.all

            let res =
                added
                |> Seq.map (fun (bp : Mdbg.Breakpoint) ->
                    let verified =
                        match bp.status with
                        | Mdbg.Bound -> true
                        | Mdbg.Unbound -> false
                    let source = Source(file, fileName)
                    bp, Breakpoint(verified, bp.line, 0., source)

                )
                |> Seq.toArray

            x.brks.[fileName] <- [| yield! rest; yield! res |> Seq.map fst |]
            let body =
                createObj [
                    "breakpoints" ==> (res |> Collections.Array.map snd)
                ]
            response.body <- body
            x.sendResponse(response)
        } |> ignore

let start () =
    DebugSession.run $ IonideDebugger
    ()

start ()
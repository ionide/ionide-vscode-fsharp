[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Ionide.VSCode.Debugger

open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.DebugSession

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
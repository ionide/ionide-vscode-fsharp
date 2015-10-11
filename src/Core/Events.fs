namespace Ionide.VSCode.FSharp

open DTO
open System

[<ReflectedDefinition>]
module Observable =
    let once (callback : 'T -> unit) (source : IObservable<'T>) =
        let sub : IDisposable option ref = ref None
        sub := source
               |> Observable.subscribe (fun t ->
                   callback t
                   (!sub).Value.Dispose ()) |> Some

[<ReflectedDefinition>]
module Events =
    let CompilerLocationEvent = Event<CompilerLocation> ()
    let HelptextEvent = Event<HelptextResult> ()
    let CompletionEvent = Event<CompletionResult> ()
    let SymbolUseEvent = Event<SymbolUseResult> ()
    let TooltipEvent = Event<TooltipResult> ()
    let ToolbarEvent = Event<TooltipResult> ()
    let ParseEvent = Event<ParseResult> ()
    let FindDeclarationEvent = Event<FindDeclarationResult> ()

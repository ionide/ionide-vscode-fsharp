namespace Ionide.VSCode.FSharp

open DTO

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

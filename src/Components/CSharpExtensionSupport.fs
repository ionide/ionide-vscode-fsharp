namespace Ionide.VSCode.FSharp

open Fable.Import.VSCode.Vscode

module CSharpExtension =

    let private msCSharpExtensionName = "ms-vscode.csharp"
    let private openvsixCSharpExtensionName = "ms-vscode.csharp"

    let mutable private hasLookedForCSharp = false
    let mutable private hasCSharp = false
    let mutable private csharpExtension: Extension<obj> = null
    let private csharpAvailableContext: bool -> unit =
        let fn = Context.cachedSetter "fsharp.debuggerAvailable"
        fun value ->
            hasCSharp <- value
            fn value

    let isCSharpAvailable () = hasCSharp

    let tryFindCSharpExtension() =
        if hasLookedForCSharp
        then hasCSharp
        else
            match extensions.getExtension msCSharpExtensionName with
            | None ->
                csharpAvailableContext false
            | Some e ->
                csharpExtension <- e
                csharpAvailableContext true
            hasLookedForCSharp <- true
            hasCSharp

    let warnAboutMissingCSharpExtension() = ()

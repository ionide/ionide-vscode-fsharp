namespace Ionide.VSCode.FSharp

open Fable.Import.VSCode.Vscode

module CSharpExtension =

    let private msCSharpExtensionName = "ms-vscode.csharp"
    let private openvsixCSharpExtensionName = "muhammad-sammy.csharp"

    let private resolvedCSharpExtensionName =
        if env.appName = "VS Code" then
            msCSharpExtensionName
        else
            openvsixCSharpExtensionName

    let private extensionLink = $"[C#](vscode:extension/{resolvedCSharpExtensionName})"

    let mutable private hasLookedForCSharp = false
    let mutable private hasCSharp = false
    let mutable private csharpExtension: Extension<obj> = null
    let mutable private hasWarned = false

    let private csharpAvailableContext: bool -> unit =
        let fn = Context.cachedSetter "fsharp.debuggerAvailable"

        fun value ->
            hasCSharp <- value
            fn value

    let isCSharpAvailable () = hasCSharp

    let tryFindCSharpExtension () =
        if not hasLookedForCSharp then
            match extensions.getExtension resolvedCSharpExtensionName with
            | None -> csharpAvailableContext false
            | Some e ->
                csharpExtension <- e
                csharpAvailableContext true

            hasLookedForCSharp <- true

        hasCSharp

    let warnAboutMissingCSharpExtension () =
        if not hasWarned then
            window.showWarningMessage (
                $"The {extensionLink} extension isn't installed, so debugging and some build tools will not be available. Consider installing the {extensionLink} extension to enable those features."
            )
            |> ignore

            hasWarned <- true

    let activate () =
        // when extensions are installed or removed we need to update our state for the C# extension
        // so enablement/disablement works correctly
        extensions.onDidChange.Invoke(fun _ ->
            tryFindCSharpExtension () |> ignore
            None)

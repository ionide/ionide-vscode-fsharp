namespace Ionide.VSCode.FSharp

open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode

module CSharpExtension =

    let private msCSharpExtensionName = "ms-dotnettools.csharp"
    let private openvsixCSharpExtensionName = "muhammad-sammy.csharp"

    let private resolvedCSharpExtensionName =
        if env.appName = "Visual Studio Code" then
            msCSharpExtensionName
        else
            openvsixCSharpExtensionName

    let mutable private hasLookedForCSharp = false
    let mutable private hasCSharp = false
    let mutable private csharpExtension: Extension<obj> = null
    let mutable private hasWarned = false

    let private csharpAvailableContext: bool -> unit =
        let fn = Context.cachedSetter "fsharp.debugger.available"

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
                $"The C# extension isn't installed, so debugging and some build tools will not be available. Consider installing the C# extension to enable those features.",
                [| "Install C# Extension" |]
            )
            |> Promise.ofThenable
            |> Promise.bind (fun c ->
                if c = Some "Install C# Extension" then
                    commands.executeCommand ("extension.open", [| Some(box resolvedCSharpExtensionName) |])
                    |> Promise.ofThenable
                else
                    Promise.empty)
            |> Promise.catch (fun e ->
                printfn $"Error installing C# extension: {Fable.Core.JS.JSON.stringify e}"
                Promise.empty)
            |> ignore<Fable.Core.JS.Promise<_>>

            hasWarned <- true

    let private notifyUserThatDebuggingWorks () =
        window.showInformationMessage (
            $"The C# extension is installed, so debugging and build tools are now available for F# projects."
        )
        |> ignore<Thenable<_>>

    let activate (context: ExtensionContext) =
        // when extensions are installed or removed we need to update our state for the C# extension
        // so enablement/disablement works correctly
        context.Subscribe(
            extensions.onDidChange.Invoke(fun _ ->
                let previousCSharpValue = hasCSharp
                hasLookedForCSharp <- false
                let currentCSharpValue = tryFindCSharpExtension ()

                match previousCSharpValue, currentCSharpValue with
                | false, true -> notifyUserThatDebuggingWorks ()
                | true, false ->
                    hasWarned <- false
                    warnAboutMissingCSharpExtension ()
                | _ -> ()

                None)
        )

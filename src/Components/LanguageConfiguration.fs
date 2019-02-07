namespace Ionide.VSCode.FSharp

open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Ionide.VSCode.Helpers

module LanguageConfiguration =

    let mutable private reference : Disposable option = None

    let indentationRules =
        jsOptions<IndentationRule> (fun o ->
            o.increaseIndentPattern <- JS.RegExp.Create("""^(\s*(module|type|let|static member|member)\b.*=\s*)$|^(\s*(with get|and set)\b.*=.*)$|^(\s*(if|elif|then|else|static member|member)).*$""")

            o.decreaseIndentPattern <- JS.RegExp.Create("""^(\s*(else|elif|and)).*$""")
        )

    let setLanguageConfiguration (triggerNotification : bool) (context : ExtensionContext) =
        // Config always setted
        let config =
            jsOptions<LanguageConfiguration> (fun o ->
                o.onEnterRules <- Some <| ResizeArray<OnEnterRule>(
                    [|
                        // Doc single-line comment
                        // Example: ///
                        jsOptions<OnEnterRule> (fun rule ->
                            rule.action <- jsOptions<EnterAction>(fun action ->
                                action.indentAction <- IndentAction.Indent
                                action.appendText <- Some "/// "
                            )
                            rule.beforeText <- JS.RegExp.Create("^\s*\/{3}.*$")
                        )

                    |]
                )
            )

        let activateSmartIndent = "FSharp.smartIndent" |> Configuration.get false

        if activateSmartIndent then
            config.indentationRules <- Some indentationRules

        match reference with
        | Some oldReference ->
            // Disable previous language configuration if there was one
            oldReference.dispose() |> ignore
        | None ->
            ()

        reference <- Some <| vscode.languages.setLanguageConfiguration("fsharp", config)

        // Notify the user if needed
        if triggerNotification then
            let msg =
                if activateSmartIndent then
                    "Smart indent has been activated for F#"
                else
                    "Smart indent has been deactivated for F#"

            vscode.window.showInformationMessage(msg) |> ignore

        context.subscriptions.Add(reference)

    let onDidChangeConfiguration (ev : ConfigurationChangeEvent) (context : ExtensionContext) =
        let triggerNotification = ev.affectsConfiguration("FSharp.smartIndent")
        setLanguageConfiguration triggerNotification context

    let activate (context : ExtensionContext) =
        // We listen for config change so we can update on the fly the language configuration
        workspace.onDidChangeConfiguration $ (onDidChangeConfiguration, context, context.subscriptions) |> ignore

        setLanguageConfiguration false context

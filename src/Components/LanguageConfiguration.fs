namespace Ionide.VSCode.FSharp

open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Ionide.VSCode.Helpers

module LanguageConfiguration =

    let activate (context : ExtensionContext) =
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

        JS.console.log config.onEnterRules.Value.[0].beforeText

        context.subscriptions.Add(vscode.languages.setLanguageConfiguration("fsharp", config))

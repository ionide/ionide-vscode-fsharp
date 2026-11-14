module Ionide.VSCode.FSharp.InlayHints

open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open Fable.Core.JsInterop

let private logger =
    ConsoleAndOutputChannelLogger(Some "InlayHints", Level.DEBUG, None, Some Level.DEBUG)

module Config =
    let typeAnnotationsEnabled = "FSharp.inlayHints.typeAnnotations"
    let parameterNamesEnabled = "FSharp.inlayHints.parameterNames"
    let disableLongTooltip = "FSharp.inlayHints.disableLongTooltip"
    let editorInlayHintsEnabled = "editor.inlayHints.enabled"


module Commands =
    let hideTypeAnnotations = "fsharp.inlayHints.hideTypeAnnotations"
    let hideParameterNames = "fsharp.inlayHints.hideParameterNames"
    let hideAll = "fsharp.inlayHints.hideAll"
    let setToToggle = "fsharp.inlayHints.setToToggle"
    let disableLongTooltip = "fsharp.inlayHints.disableLongTooltip"

let setLocalOrGlobalConfiguration configKey configValue =
    if isUndefined workspace.workspaceFolders then
        // do the config update at the user-level since no workspace is open
        Configuration.setGlobal configKey configValue |> box |> Some
    else
        // do the config update at the workspace level
        Configuration.set configKey configValue |> box |> Some

let setLocalOrGlobalConfigurationForFSharpLanguage configKey configValue =
    if isUndefined workspace.workspaceFolders then
        // do the config update at the user-level since no workspace is open
        Configuration.setForFsharpLanguageOnly configKey configValue ConfigurationTarget.Global
        |> box
        |> Some
    else
        // do the config update at the workspace level
        Configuration.setForFsharpLanguageOnly configKey configValue ConfigurationTarget.Workspace
        |> box
        |> Some


let activate (context: ExtensionContext) =
    commands.registerCommand (
        Commands.disableLongTooltip,
        (fun _ ->
            setLocalOrGlobalConfiguration Config.disableLongTooltip (Some true)
            |> box
            |> Some)
    )
    |> context.Subscribe

    commands.registerCommand (
        Commands.setToToggle,
        (fun _ ->
            setLocalOrGlobalConfiguration Config.editorInlayHintsEnabled (Some "offUnlessPressed")
            |> box
            |> Some)
    )
    |> context.Subscribe

    commands.registerCommand (
        Commands.hideAll,
        (fun _ ->
            setLocalOrGlobalConfigurationForFSharpLanguage Config.editorInlayHintsEnabled (Some "off")
            |> box
            |> Some)
    )
    |> context.Subscribe

    commands.registerCommand (
        Commands.hideParameterNames,
        (fun _ ->
            setLocalOrGlobalConfiguration Config.parameterNamesEnabled (Some false)
            |> box
            |> Some)
    )
    |> context.Subscribe

    commands.registerCommand (
        Commands.hideTypeAnnotations,
        (fun _ ->
            setLocalOrGlobalConfiguration Config.typeAnnotationsEnabled (Some false)
            |> box
            |> Some)
    )
    |> context.Subscribe

    logger.Info "Activating F# inlay hints"
    ()

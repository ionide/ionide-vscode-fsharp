[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Ionide.VSCode.FSharp

open System
open System.Text.RegularExpressions
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.vscode
open Ionide.VSCode.Helpers
open Ionide.VSCode.FSharp

let activate (context: ExtensionContext) =
    context.subscriptions
    let df = createEmpty<DocumentFilter>
    df.language <- Some "fsharp"
    let df' : DocumentSelector = df |> U3.Case2

    let legacyFsi = "FSharp.legacyFSI" |> Configuration.get false
    let resolve = "FSharp.resolveNamespaces" |> Configuration.get false
    let solutionExploer = "FSharp.enableTreeView" |> Configuration.get true

    let init = DateTime.Now

    Project.clearCacheIfOutdated ()

    LanguageService.start ()
    |> Promise.onSuccess (fun _ ->
        let progressOpts = createEmpty<ProgressOptions>
        progressOpts.location <- ProgressLocation.Window
        window.withProgress(progressOpts, (fun p ->
            let pm = createEmpty<ProgressMessage>
            pm.message <- "Loading current project"
            p.report pm
            Errors.activate context
            |> Promise.onSuccess(fun _ ->
                pm.message <- "Loading all projects"
                p.report pm
                CodeLens.activate df' context
                Linter.activate df' context
            )
            |> Promise.onSuccess(fun _ -> if solutionExploer then SolutionExplorer.activate ())
            |> Promise.bind(fun _ -> Project.activate ())


        ))
        |> Promise.onSuccess (fun n ->
            let e = DateTime.Now - init
            printfn "Startup took: %f ms" e.TotalMilliseconds
        )
        |> ignore

        Tooltip.activate df' context
        Autocomplete.activate df' context
        ParameterHints.activate df' context
        Definition.activate df' context
        Reference.activate df' context
        Symbols.activate df' context
        Highlights.activate df' context
        Rename.activate df' context
        WorkspaceSymbols.activate df' context
        QuickInfo.activate context
        QuickFix.activate df' context
        if resolve then ResolveNamespaces.activate df' context
        UnionCaseGenerator.activate df' context
        Help.activate context
        Expecto.activate context
        MSBuild.activate context
        SignatureData.activate context
    )
    |> Promise.catch (fun error -> promise { () }) // prevent unhandled rejected promises
    |> ignore

    Forge.activate context
    if legacyFsi then LegacyFsi.activate context else Fsi.activate context

    ()

let deactivate(disposables: Disposable[]) =
    LanguageService.stop ()

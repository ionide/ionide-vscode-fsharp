[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Ionide.VSCode.FSharp

open System
open System.Text.RegularExpressions
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.vscode
open Ionide.VSCode.Helpers
open Ionide.VSCode.FSharp

let activate(disposables: Disposable[]) =
    let df = createEmpty<DocumentFilter>
    df.language <- Some "fsharp"
    let df' : DocumentSelector = df |> U3.Case2

    let legacyFsi = "FSharp.legacyFSI" |> Configuration.get false
    let resolve = "FSharp.resolveNamespaces" |> Configuration.get false
    let solutionExploer = "FSharp.enableTreeView" |> Configuration.get true

    let init = DateTime.Now
    LanguageService.start ()
    |> Promise.onSuccess (fun _ ->
        let progressOpts = createEmpty<ProgressOptions>
        progressOpts.location <- ProgressLocation.Window
        window.withProgress(progressOpts, (fun p ->
            let pm = createEmpty<ProgressMessage>
            pm.message <- "Loading current project"
            p.report pm
            Errors.activate disposables
            |> Promise.onSuccess(fun _ ->
                pm.message <- "Loading all projects"
                p.report pm
                CodeLens.activate df' disposables
                Linter.activate df' disposables
            )
            |> Promise.onSuccess(fun _ -> if solutionExploer then SolutionExplorer.activate ())
            |> Promise.bind(fun _ -> Project.activate ())


        ))
        |> Promise.onSuccess (fun n ->
            let e = DateTime.Now - init
            printfn "Startup took: %f ms" e.TotalMilliseconds
        )
        |> ignore

        Tooltip.activate df' disposables
        Autocomplete.activate df' disposables
        ParameterHints.activate df' disposables
        Definition.activate df' disposables
        Reference.activate df' disposables
        Symbols.activate df' disposables
        Highlights.activate df' disposables
        Rename.activate df' disposables
        WorkspaceSymbols.activate df' disposables
        QuickInfo.activate disposables
        QuickFix.activate df' disposables
        if resolve then ResolveNamespaces.activate df' disposables
        UnionCaseGenerator.activate df' disposables
        Help.activate disposables
        Expecto.activate disposables
        MSBuild.activate disposables
        SignatureData.activate disposables
    )
    |> Promise.catch (fun error -> promise { () }) // prevent unhandled rejected promises
    |> ignore

    Forge.activate disposables
    if legacyFsi then LegacyFsi.activate disposables else Fsi.activate disposables

    ()

let deactivate(disposables: Disposable[]) =
    LanguageService.stop ()

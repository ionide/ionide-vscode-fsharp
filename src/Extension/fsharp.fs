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

    LanguageService.start ()
    |> Promise.onSuccess (fun _ ->
        Errors.activate disposables
        |> Promise.onSuccess(fun _ ->
            CodeLens.activate df' disposables
            Linter.activate df' disposables
        )
        |> Promise.bind(fun _ -> Project.activate ())
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
    )
    |> Promise.catch (fun error -> promise { () }) // prevent unhandled rejected promises
    |> ignore

    Forge.activate disposables
    if legacyFsi then LegacyFsi.activate disposables else Fsi.activate disposables
    WebPreview.activate disposables

    ()

let deactivate(disposables: Disposable[]) =
    LanguageService.stop ()

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Ionide.VSCode.FSharp

open System
open System.Text.RegularExpressions
open Fable.Core
open Fable.Import.vscode
open Ionide.VSCode.Helpers
open Ionide.VSCode.FSharp

let activate(disposables: Disposable[]) =
    let df = createEmpty<DocumentFilter>
    df.language <- Some "fsharp"
    let df' : DocumentSelector = df |> U3.Case2


    LanguageService.start ()
    Linter.activate disposables
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

    Fsi.activate disposables
    QuickInfo.activate disposables
    WebPreview.activate disposables
    Forge.activate disposables

    ()

let deactivate(disposables: Disposable[]) =
    LanguageService.stop ()


[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Ionide.VSCode.FSharp

open System
open System.Text.RegularExpressions
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.vscode
open Ionide.VSCode.Helpers
open Ionide.VSCode.FSharp

let internal languageServiceListeningCallback disposables df' =
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
    QuickInfo.activate disposables

let activate(disposables: Disposable[]) =
    let df = createEmpty<DocumentFilter>
    df.language <- Some "fsharp"
    let df' : DocumentSelector = df |> U3.Case2

    LanguageService.start (fun () -> languageServiceListeningCallback disposables df')
    |> ignore
    Forge.activate disposables
    Fsi.activate disposables
    WebPreview.activate disposables


    ()

let deactivate(disposables: Disposable[]) =
    LanguageService.stop ()


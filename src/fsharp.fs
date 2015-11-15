[<ReflectedDefinition>]
module Ionide.VSCode

open System
open System.Text.RegularExpressions
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.fs
open FunScript.TypeScript.child_process
open FunScript.TypeScript.vscode

open Ionide.VSCode.FSharp

type FSharp() =
    member x.activate(disposables: Disposable[]) =
        LanguageService.start ()
        let df = createEmpty<DocumentFilter> ()
        df.language <- "fsharp"
        let df' = [|df|]
        Linter.activate disposables
        Tooltip.activate df' disposables
        Autocomplete.activate df' disposables
        ParameterHints.activate df' disposables
        Definition.activate df' disposables
        Reference.activate df' disposables
        Outline.activate df' disposables
        Fsi.activate disposables  
        QuickInfo.activate disposables
        ()

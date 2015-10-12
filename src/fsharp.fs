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
        Linter.activate disposables
        Tooltip.activate disposables
        Autocomplete.activate disposables
        ParameterHints.activate disposables
        ()

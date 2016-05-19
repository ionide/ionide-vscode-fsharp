[<ReflectedDefinition>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Ionide.VSCode

open System
open System.Text.RegularExpressions
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.fs
open FunScript.TypeScript.child_process
open FunScript.TypeScript.vscode

open Ionide.VSCode.FSharp
open Ionide.VSCode.Helpers

type FSharp() =
    member x.activate(disposables: Disposable[]) =
        let df = createEmpty<DocumentFilter> ()
        df.language <- "fsharp"
        let df' = [|df|]
        
        LanguageService.start ()
        Project.activate () |> ignore
        Linter.activate disposables 
        Tooltip.activate df' disposables
        Autocomplete.activate df' disposables
        ParameterHints.activate df' disposables
        Definition.activate df' disposables
        Reference.activate df' disposables 
        Symbols.activate df' disposables
        Highlights.activate df' disposables
        Rename.activate df' disposables
        Fsi.activate disposables
        QuickInfo.activate disposables
        FSharpFormatting.activate disposables
        WebPreview.activate disposables
        Forge.activate disposables
        
        ()

    member x.deactivate(disposables: Disposable[]) =
        LanguageService.stop ()


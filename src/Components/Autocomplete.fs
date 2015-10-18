namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.Modes

open DTO

[<ReflectedDefinition>]
module Autocomplete =
    let private createProvider () =
        let provider = createEmpty<Modes.ISuggestSupport> ()
        provider.suggest <- (fun doc pos _ ->
            LanguageService.completion (doc.getPath ()) (int pos.line) (int pos.character)
            |> Promise.success (fun (o : CompletionResult) ->
                o.Data |> Array.map (fun c ->
                    let range = doc.getWordRangeAtPosition pos
                    let length = if JS.isDefined range then range._end.character - range.start.character else 0.
                    let result = createEmpty<ISuggestions> ()
                    let sug = createEmpty<ISuggestion> ()
                    sug._type <- c.Glyph.ToLower()
                    sug.label <- c.Name
                    sug.codeSnippet <- c.Code
                    result.currentWord <- c.Name
                    result.suggestions <- [| sug; |]
                    result.incomplete <- true
                    result.overwriteBefore <- length
                    result.overwriteAfter <- 0.
                    result)
                )
            |> Promise.toThenable)
        provider.getSuggestionDetails <- (fun doc pos sug _ ->
            LanguageService.helptext sug.label
            |> Promise.success (fun (o : HelptextResult) ->
                let res = (o.Data.Overloads |> Array.fold (fun acc n -> (n |> Array.toList) @ acc ) []).Head.Signature
                sug.documentationLabel <- res
                sug
                )
            |> Promise.toThenable
        )
        provider

    let activate (disposables: Disposable[]) =
        vscode.Modes.Globals.SuggestSupport.register("fsharp", createProvider())
        |> ignore

        ()

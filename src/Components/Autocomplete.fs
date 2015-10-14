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
        //provider.triggerCharacters <- [|"*"|]
        //provider.excludeTokens <- [||]
        provider.suggest <- (fun doc pos _ ->
            LanguageService.completion (doc.getPath ()) (int pos.line) (int pos.character)
            |> Promise.success (fun o ->
                o.Data |> Array.map (fun c ->
                    let result = createEmpty<ISuggestions> ()
                    let sug = createEmpty<ISuggestion> ()
                    sug._type <- c.Glyph
                    sug.label <- c.Name
                    sug.codeSnippet <- c.Name
                    result.currentWord <- c.Name
                    result.suggestions <- [| sug |]
                    result)
                )
            |> Promise.toThenable)
        provider

    let activate (disposables: Disposable[]) =
        vscode.Modes.Globals.SuggestSupport.register("fsharp", createProvider())
        |> ignore

        ()

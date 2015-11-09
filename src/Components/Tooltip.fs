namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.languages

open DTO

open Ionide.VSCode.Helpers

[<ReflectedDefinition>]
module Tooltip =

    let private createProvider () =
        let provider = createEmpty<HoverProvider> ()

        let mapResult (doc : TextDocument) (pos : Position) o =
            let range = doc.getWordRangeAtPosition pos
            let res = (o.Data |> Array.fold (fun acc n -> (n |> Array.toList) @ acc ) []).Head.Signature
            let content =
                res.Split('\n')
                |> Array.filter((<>) "")
                |> Array.map (fun n ->
                    let el = createEmpty<MarkedString> ()
                    el.value <- n
                    el)
            let result = createEmpty<Hover> ()
            result.range <- range
            result.contents <- content
            result

        provider.``provideHover <-``(fun doc pos _ ->
            LanguageService.tooltip (doc.fileName) (int pos.line + 1) (int pos.character + 1)
            |> Promise.success (mapResult doc pos)
            |> Promise.toThenable )
        provider

    let activate selector (disposables: Disposable[]) =
        Globals.registerHoverProvider(selector, createProvider())
        |> ignore
        ()

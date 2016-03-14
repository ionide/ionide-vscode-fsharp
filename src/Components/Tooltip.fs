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
            let res = (o.Data |> Array.fold (fun acc n -> (n |> Array.toList) @ acc ) []).Head
            
            let markStr lang (value:string) = 
                let ms = createEmpty<MarkedString> ()
                ms.language <- lang; ms.value <- value.Trim()
                ms
            let sigContent =
                res.Signature.Split('\n')
                |> Array.filter(String.IsNullOrWhiteSpace>>not)
                |> Array.map (fun n -> 
                    let el = createEmpty<MarkedString> ()
                    el.language <- "fsharp"; el.value <- n; el)
            let commentContent =
                res.Comment.Split('\n')
                |> Array.filter(String.IsNullOrWhiteSpace>>not) 
                |> Array.mapi (fun i n -> 
                    let el = createEmpty<MarkedString> ()
                    el.value <- if i = 0 && not(String.IsNullOrWhiteSpace n) 
                                then "\n" + n.Trim() 
                                else n.Trim()
                    el.language <- "markdown"; el)
            let result = createEmpty<Hover> ()
            result.range <- range
            result.contents <- Array.append sigContent commentContent
            result
            
        let logError (o : obj) = 
            Globals.console.warn o
            null |> unbox<Hover>

        provider.``provideHover <-``(fun doc pos _ ->
            LanguageService.tooltip (doc.fileName) (int pos.line + 1) (int pos.character + 1)
            |> Promise.either (mapResult doc pos) logError
            |> Promise.toThenable )
        provider

    let activate selector (disposables: Disposable[]) =
        Globals.registerHoverProvider(selector, createProvider())
        |> ignore
        ()

namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.Modes

open DTO

[<ReflectedDefinition>]
module Tooltip =

    let private createProvider () =
        let provider = createEmpty<IExtraInfoSupport> ()
        provider.``computeInfo <-`` (fun doc pos _ ->
            LanguageService.tooltip (doc.getPath ()) (int pos.line) (int pos.character)
            |> Promise.success (fun o ->
                let range = doc.getWordRangeAtPosition pos
                let res = (o.Data |> Array.fold (fun acc n -> (n |> Array.toList) @ acc ) []).Head.Signature
                let htmlContent =
                    res.Split('\n')
                    |> Array.filter((<>) "")
                    |> Array.map (fun n ->
                        let el = createEmpty<IHTMLContentElement> ()
                        el.tagName <- "p"
                        el.text <- n
                        el)
                let result = createEmpty<IComputeExtraInfoResult> ()
                result.range <- range
                result.htmlContent <- htmlContent
                result )
            |> Promise.toThenable )
        provider

    let activate (disposables: Disposable[]) =
        Globals.ExtraInfoSupport.register("fsharp", createProvider())
        |> ignore
        ()

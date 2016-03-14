namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.languages

open DTO
open Ionide.VSCode.Helpers

[<ReflectedDefinition>]
module Highlights =
    let private createProvider () =
        let provider = createEmpty<DocumentHighlightProvider> ()

        let mapResult (o : SymbolUseResult) =
            o.Data.Uses |> Array.map (fun d ->
                let res = createEmpty<DocumentHighlight> ()
                res.range <- Range.Create(float d.StartLine - 1., float d.StartColumn - 1., float d.EndLine - 1., float d.EndColumn - 1.)
                res.kind <- (0 |> unbox)
                res )

        provider.``provideDocumentHighlights <-`` (fun doc pos _ ->
            LanguageService.parse doc.fileName (doc.getText ())
            |> Promise.bind (fun _ -> LanguageService.symbolUse (doc.fileName) (int pos.line + 1) (int pos.character + 1))
            |> Promise.success mapResult
            |> Promise.toThenable )
        provider

    let activate selector (disposables: Disposable[]) =
        Globals.registerDocumentHighlightProvider(selector, createProvider())
        |> ignore

        ()

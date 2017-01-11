namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node



open DTO
open Ionide.VSCode.Helpers

module Highlights =
    let private createProvider () =

        let mapResult (o : SymbolUseResult) =
            if isNotNull o then
                o.Data.Uses |> Array.map (fun d ->
                    let res = createEmpty<DocumentHighlight>
                    res.range <- CodeRange.fromSymbolUse d
                    res.kind <- unbox 0
                    res)
                |> ResizeArray
            else
                ResizeArray ()


        { new DocumentHighlightProvider
          with
            member this.provideDocumentHighlights(doc, pos, ct) =
                promise {
                    let! res = LanguageService.symbolUse (doc.fileName) (int pos.line + 1) (int pos.character + 1)
                    return mapResult res
                } |> Case2
        }

    let activate selector (disposables: Disposable[]) =
        languages.registerDocumentHighlightProvider(selector, createProvider())
        |> ignore

        ()

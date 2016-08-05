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
            if o |> unbox <> null then
                o.Data.Uses |> Array.map (fun d ->
                    let res = createEmpty<DocumentHighlight>
                    res.range <- Range(float d.StartLine - 1., float d.StartColumn - 1., float d.EndLine - 1., float d.EndColumn - 1.)
                    res.kind <- (0 |> unbox)
                    res )
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

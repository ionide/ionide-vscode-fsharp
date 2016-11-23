namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers

module Reference =
    let private createProvider () =

        let mapResult (doc : TextDocument) (o : SymbolUseResult) =
            if isNotNull o then
                o.Data.Uses |> Array.map (fun s ->
                    let loc = createEmpty<Location>
                    loc.range <- CodeRange.fromSymbolUse s
                    loc.uri <- Uri.file s.FileName
                    loc  )
                |> ResizeArray
            else
                ResizeArray ()

        { new ReferenceProvider
          with
            member this.provideReferences(doc, pos, _, _) =
                promise {
                    let! res = LanguageService.symbolUseProject (doc.fileName) (int pos.line + 1) (int pos.character + 1)
                    return mapResult doc res
                } |> Case2
        }

    let activate selector (disposables: Disposable[]) =
        languages.registerReferenceProvider(selector, createProvider())
        |> ignore
        ()

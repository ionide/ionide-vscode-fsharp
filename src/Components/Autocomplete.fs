namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers

open DTO
open Ionide.VSCode.Helpers

module Autocomplete =
    let private createProvider () =
        let provider = createEmpty<CompletionItemProvider>

        let convertToInt code =
            match code with
            | "C" -> 6      (*  CompletionItemKind.Class      *)
            | "E" -> 12     (*  CompletionItemKind.Enum       *)
            | "S" -> 6      (*  CompletionItemKind.Value      *)
            | "I" -> 7      (*  CompletionItemKind.Interface  *)
            | "N" -> 8      (*  CompletionItemKind.Module     *)
            | "M" -> 1      (*  CompletionItemKind.Method     *)
            | "P" -> 9      (*  CompletionItemKind.Property   *)
            | "F" -> 4      (*  CompletionItemKind.Field      *)
            | "T" -> 6      (*  CompletionItemKind.Class      *)
            | _   -> 0

        let mapCompletion (doc : TextDocument) (pos : Position) (o : CompletionResult) =
            if o |> unbox <> null then
                o.Data |> Array.map (fun c ->
                    let range = doc.getWordRangeAtPosition pos
                    let length = if JS.isDefined range then range.``end``.character - range.start.character else 0.
                    let result = createEmpty<CompletionItem>
                    result.kind <- c.GlyphChar |> convertToInt |> unbox
                    result.label <- c.Name
                    result.insertText <- c.ReplacementText
                    result)
                |> ResizeArray
            else
                ResizeArray ()

        let mapHelptext (sug : CompletionItem) (o : HelptextResult) =
            let res = (o.Data.Overloads |> Array.fold (fun acc n -> (n |> Array.toList) @ acc ) []).Head
            sug.documentation <- res.Comment
            sug.detail <- res.Signature
            sug


        { new CompletionItemProvider
          with
            member this.provideCompletionItems(doc, pos, ct) =
                promise {
                    let ln = doc.lineAt pos.line
                    let! res = LanguageService.completion (doc.fileName) ln.text (int pos.line + 1) (int pos.character + 1)
                    return mapCompletion doc pos res
                } |> Case2

            member this.resolveCompletionItem(sug, ct) =
                promise {
                    let! res = LanguageService.helptext sug.label
                    return mapHelptext sug res
                } |> Case2
            }

    let activate selector (disposables: Disposable[]) =
        languages.registerCompletionItemProvider (selector, createProvider())
        |> ignore
        ()

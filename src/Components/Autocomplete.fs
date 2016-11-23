namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers

open DTO
open Ionide.VSCode.Helpers

module Autocomplete =

    let private createProvider () =
        let provider = createEmpty<CompletionItemProvider>

        let convertToKind code =
            match code with
            | "C" -> CompletionItemKind.Class
            | "E" -> CompletionItemKind.Enum
            | "S" -> CompletionItemKind.Value
            | "I" -> CompletionItemKind.Interface
            | "N" -> CompletionItemKind.Module
            | "M" -> CompletionItemKind.Method
            | "P" -> CompletionItemKind.Property
            | "F" -> CompletionItemKind.Field
            | "T" -> CompletionItemKind.Class
            | "K" -> CompletionItemKind.Keyword
            | _   -> 0 |> unbox

        let mapCompletion (doc : TextDocument) (pos : Position) (o : CompletionResult) =
            let lineStr = doc.getText(Range(pos.line, 0., pos.line, 1000. ))
            let chars = lineStr.ToCharArray ()
            let noSpaces = chars |> Array.filter ((<>) ' ')
            let spacesCount = chars |> Array.take (int pos.character) |> Array.filter ((=) ' ') |> Array.length
            let index = int pos.character - spacesCount - 1
            let prevChar = noSpaces.[index]

            if isNotNull o then
                o.Data |> Array.choose (fun c ->
                    if prevChar = '.' && c.GlyphChar = "K" then
                        None
                    else
                        let range = doc.getWordRangeAtPosition pos
                        let length = if JS.isDefined range then range.``end``.character - range.start.character else 0.
                        let result = createEmpty<CompletionItem>
                        result.kind <- c.GlyphChar |> convertToKind |> unbox
                        result.label <- c.Name
                        result.insertText <- c.ReplacementText
                        Some result)

                |> ResizeArray
            else
                ResizeArray ()

        let mapHelptext (sug : CompletionItem) (o : HelptextResult) =
            let res = (o.Data.Overloads |> Array.collect id).[0]
            sug.documentation <- Markdown.replaceXml res.Comment
            sug.detail <- res.Signature
            sug


        { new CompletionItemProvider
          with
            member this.provideCompletionItems(doc, pos, ct) =
                promise {
                    let setting = "FSharp.keywordsAutocomplete" |> Configuration.get true
                    let ln = doc.lineAt pos.line
                    let! res = LanguageService.completion (doc.fileName) ln.text (int pos.line + 1) (int pos.character + 1) setting
                    return mapCompletion doc pos res
                } |> Case2

            member this.resolveCompletionItem(sug, ct) =
                promise {
                    let! res = LanguageService.helptext sug.label
                    return mapHelptext sug res
                } |> Case2
            }

    let activate selector (disposables: Disposable[]) =
        languages.registerCompletionItemProvider (selector, createProvider(), ".")
        |> ignore
        ()

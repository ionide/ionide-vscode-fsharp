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

    [<Literal>]
    let getWordAtPositionJS =
        """
    function getWordAt(str, pos) {
    str = String(str);
    pos = Number(pos) >>> 0;

    var left = str.slice(0, pos + 1).search(/\S+$/),
        right = str.slice(pos).search(/\s/);

    if (right < 0) {
        return str.slice(left);
    }
    return str.slice(left, right + pos);
}
getWordAt($0, $1)
        """

    [<Emit(getWordAtPositionJS)>]
    let getWordAtPosition(str, post) : string = failwith "JS"

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
            if o |> unbox <> null then
                o.Data |> Array.choose (fun c ->
                    let lineStr = doc.getText(Range(pos.line, 0., pos.line, 1000. ))
                    let word = getWordAtPosition(lineStr, pos.character)
                    Browser.console.log word
                    if word <> "" then

                        if word.Contains "." && c.GlyphChar = "K" then
                            None
                        else
                            let range = doc.getWordRangeAtPosition pos
                            let length = if JS.isDefined range then range.``end``.character - range.start.character else 0.
                            let result = createEmpty<CompletionItem>
                            result.kind <- c.GlyphChar |> convertToKind |> unbox
                            result.label <- c.Name
                            result.insertText <- c.ReplacementText
                            Some result
                    else
                        let length = 0.
                        let result = createEmpty<CompletionItem>
                        result.kind <- c.GlyphChar |> convertToKind |> unbox
                        result.label <- c.Name
                        result.insertText <- c.ReplacementText
                        Some result)

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
        languages.registerCompletionItemProvider (selector, createProvider(), ".")
        |> ignore
        ()

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
                        let result = createEmpty<CompletionItem>
                        result.kind <- c.GlyphChar |> convertToKind |> unbox
                        result.insertText <- c.ReplacementText
                        result.sortText <- c.Name
                        if JS.isDefined c.NamespaceToOpen then
                            result.label <- sprintf "%s (open %s)" c.Name c.NamespaceToOpen
                        else
                            result.label <- c.Name

                        Some result)

                |> ResizeArray
            else
                ResizeArray ()

        let mapHelptext (sug : CompletionItem) (o : HelptextResult) =
            if isNotNull o then
                let res = (o.Data.Overloads |> Array.collect id).[0]
                sug.documentation <- res.Comment |> Markdown.createCommentBlock |> U2.Case2
                sug.detail <- res.Signature
                if JS.isDefined o.Data.AdditionalEdit then
                    let l = o.Data.AdditionalEdit.Line - 1
                    let c = o.Data.AdditionalEdit.Column
                    let t = sprintf "%sopen %s\n" (String.replicate c " ") o.Data.AdditionalEdit.Text
                    let p = Position(float l, 0.)
                    let te = TextEdit.insert(p, t)
                    sug.additionalTextEdits <- [| te |]
            sug

        { new CompletionItemProvider
          with
            member __.provideCompletionItems(doc, pos, _) =
                promise {
                    let setting = "FSharp.keywordsAutocomplete" |> Configuration.get true
                    let external = "FSharp.externalAutocomplete" |> Configuration.get true
                    let ln = doc.lineAt pos.line
                    let! res = LanguageService.completion (doc.fileName) ln.text (int pos.line + 1) (int pos.character + 1) setting external
                    return mapCompletion doc pos res
                } |> U2.Case2

            member __.resolveCompletionItem(sug, _) =
                promise {
                    let! res = LanguageService.helptext sug.sortText
                    return mapHelptext sug res
                } |> U2.Case2
            }

    let activate selector (context: ExtensionContext) =
        languages.registerCompletionItemProvider (selector, createProvider(), ".")
        |> context.subscriptions.Add
        ()

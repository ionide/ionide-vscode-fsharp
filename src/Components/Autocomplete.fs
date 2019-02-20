namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Ionide.VSCode.Helpers

open DTO

module Autocomplete =

    let private createProvider () =
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

        let mapCompletion (o : CompletionResult) =
            if isNotNull o then
                o.Data |> Array.mapi (fun id c ->
                    let result = createEmpty<CompletionItem>
                    result.kind <- c.GlyphChar |> convertToKind |> unbox
                    result.insertText <- c.ReplacementText
                    result.sortText <- sprintf "%06d" id
                    result.filterText <- c.Name
                    if JS.isDefined c.NamespaceToOpen then
                        result.label <- sprintf "%s (open %s)" c.Name c.NamespaceToOpen
                    else
                        result.label <- c.Name

                    result)

                |> ResizeArray
            else
                ResizeArray ()

        let getLineStr (doc : TextDocument) line = doc.getText(Range(line, 0., line, 1000.)).Trim()

        let adjustPosition (doc : TextDocument) typ l =
            match typ with
            | "TopModule" ->
                if l > 1 then
                    // it's an implicit module without any open declarations
                    let line = getLineStr doc (unbox (l - 2))
                    let isImpliciteTopLevelModule = not (line.StartsWith "module" && not (line.EndsWith "="))
                    if isImpliciteTopLevelModule then 1 else l
                else 1
            | "Namespace" ->
                // for namespaces the start line is start line of the first nested entity
                if l > 1 then
                    [0..l - 1]
                    |> List.mapi (fun i line -> i, getLineStr doc (unbox line))
                    |> List.tryPick (fun (i, lineStr) ->
                        if lineStr.StartsWith "namespace" then Some i
                        else None)
                    |> function
                        // move to the next line below "namespace" and convert it to F# 1-based line number
                        | Some line -> line + 2
                        | None -> l
                else 1
            | _ -> l

        let mapHelptext (doc : TextDocument) (sug : CompletionItem) (o : HelptextResult) =
            if isNotNull o then
                let res = (o.Data.Overloads |> Array.concat).[0]
                sug.documentation <- res.Comment |> Markdown.createCommentBlock |> U2.Case2
                sug.detail <- res.Signature
                if JS.isDefined o.Data.AdditionalEdit then

                    let l = (adjustPosition doc o.Data.AdditionalEdit.Type o.Data.AdditionalEdit.Line) - 1
                    let c = o.Data.AdditionalEdit.Column
                    let t = sprintf "%sopen %s\n" (String.replicate c " ") o.Data.AdditionalEdit.Text
                    let p = Position(float l, 0.)
                    let te = TextEdit.insert(p, t)
                    sug.additionalTextEdits <- [| te |]
            sug

        let hashDirectives =
            ["r", "References an assembly"
             "load", "Reads a source file, compiles it, and runs it."
             "I", "Specifies an assembly search path in quotation marks."
             "light", "Enables or disables lightweight syntax, for compatibility with other versions of ML"
             "if", "Supports conditional compilation"
             "else", "Supports conditional compilation"
             "endif", "Supports conditional compilation"
             "nowarn", "Disables a compiler warning or warnings"
             "line", "Indicates the original source code line"]

        let hashCompletions =
            hashDirectives
            |> List.map (fun (n,d) ->
                let result = createEmpty<CompletionItem>
                result.kind <- CompletionItemKind.Keyword
                result.label <- "#" + n
                result.insertText <- n
                result.filterText <- n
                result.sortText <- n
                result.documentation <- d |> Markdown.createCommentBlock |> U2.Case2
                result
            ) |> ResizeArray
        let mutable curDoc : TextDocument option = None

        { new CompletionItemProvider
          with
            member __.provideCompletionItems(doc, pos, _) =
                promise {
                    curDoc <- Some doc
                    let setting = "FSharp.keywordsAutocomplete" |> Configuration.get true
                    let external = "FSharp.externalAutocomplete" |> Configuration.get true
                    let ln = doc.lineAt pos.line
                    let r = doc.getWordRangeAtPosition pos
                    let word = doc.getText r
                    if not (ln.text.StartsWith "#" && (hashDirectives |> List.exists (fun (n,_) -> n.StartsWith word ) || word.Contains "\n" ))  then
                        let! res = LanguageService.completion (doc.fileName) ln.text (int pos.line + 1) (int pos.character + 1) setting external (int doc.version)
                        return mapCompletion res
                    else
                        return hashCompletions
                } |> U2.Case2

            member __.resolveCompletionItem(sug, _) =
                promise {
                    if JS.isDefined sug.documentation then
                        return sug
                    else
                        let! res = LanguageService.helptext sug.filterText
                        return mapHelptext curDoc.Value sug res
                } |> U2.Case2
            }


    let activate selector (context : ExtensionContext) =
        languages.registerCompletionItemProvider (selector, createProvider(), ".")
        |> context.subscriptions.Add
        ()

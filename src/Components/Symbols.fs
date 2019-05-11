namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Fable.Core.JsInterop
open DTO
open Ionide.VSCode
open Ionide.VSCode.Helpers

module Symbols =

    let private createProvider () =
        let convertToKind code =
            match code with
            | "C" -> SymbolKind.Class     (*  CompletionItemKind.Class      *)
            | "E" -> SymbolKind.Enum      (*  CompletionItemKind.Enum       *)
            | "S" -> SymbolKind.Property      (*  CompletionItemKind.Value      *)
            | "I" -> SymbolKind.Interface     (*  CompletionItemKind.Interface  *)
            | "N" -> SymbolKind.Module      (*  CompletionItemKind.Module     *)
            | "M" -> SymbolKind.Method     (*  CompletionItemKind.Method     *)
            | "P" -> SymbolKind.Property      (*  CompletionItemKind.Property   *)
            | "F" -> SymbolKind.Variable     (*  CompletionItemKind.Field      *)
            | "T" -> SymbolKind.Class      (*  CompletionItemKind.Class      *)
            | "Fc" -> SymbolKind.Function
            | _   -> 0 |> unbox

        let mapRes (doc : TextDocument) o =
            let data =
                if isNotNull o then
                    o.Data |> Array.map (fun syms ->
                        let range = CodeRange.fromDTO syms.Declaration.BodyRange
                        let oc = DocumentSymbol(syms.Declaration.Name, "", syms.Declaration.GlyphChar |> convertToKind, range, range) //TODO: Add signature
                        let ocs =
                            syms.Nested |> Array.map (fun sym ->
                                let range = CodeRange.fromDTO sym.BodyRange
                                let oc = DocumentSymbol(sym.Name, "", sym.GlyphChar |> convertToKind, range, range) //TODO: Add signature

                                oc )
                            |> Array.sortBy (fun n -> n.range.start.line)
                            |> ResizeArray
                        oc.children <- ocs
                        oc)
                    else
                        [||]
            let data = data |> Array.sortBy (fun n -> n.range.start.line)
            let head,xs = data.[0], ResizeArray (data.[1..])

            let relations =
                data.[1..]
                |> Seq.mapi (fun id n ->
                    let parent = xs |> Seq.tryFindIndexBack (fun p -> p.range.contains (!^n.range) && not (p.range.isEqual n.range))
                    parent |> Option.map (fun p -> id, p)
                )
                |> Seq.choose id

            relations
            |> Seq.iter (fun (i,p) ->
                let i = xs.[i]
                let p = xs.[p]
                p.children.Add i
            )
            let toRemove = relations |> Seq.map (fun (i,p) -> xs.[i] )
            let xs = xs |> Seq.filter (fun n -> not (toRemove |> Seq.contains n)) |> ResizeArray
            head.children.AddRange xs
            [head]

        { new DocumentSymbolProvider
          with
            member this.provideDocumentSymbols(doc, ct) =
                promise {
                    let text = doc.getText()
                    let! o = LanguageService.declarations doc.fileName text (unbox doc.version)
                    let data = mapRes doc o
                    return data |> ResizeArray
                } |> unbox
        }


    let activate selector (context : ExtensionContext) =
        languages.registerDocumentSymbolProvider(selector, createProvider())
        |> context.subscriptions.Add
        ()

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
             if isNotNull o then
                o.Data |> Array.map (fun syms ->
                    let oc = createEmpty<SymbolInformation>
                    oc.name <- syms.Declaration.Name
                    oc.kind <- syms.Declaration.GlyphChar |> convertToKind
                    oc.containerName <- syms.Declaration.Glyph
                    let loc = createEmpty<Location>
                    loc.range <- CodeRange.fromDTO syms.Declaration.BodyRange
                    loc.uri <- Uri.file doc.fileName
                    oc.location <- loc
                    let ocs =  syms.Nested |> Array.map (fun sym ->
                        let oc = createEmpty<SymbolInformation>
                        oc.name <- sym.Name
                        oc.kind <- sym.GlyphChar |> convertToKind
                        oc.containerName <- sym.Glyph
                        let loc = createEmpty<Location>
                        loc.range <- CodeRange.fromDTO sym.BodyRange
                        loc.uri <- Uri.file doc.fileName
                        oc.location <- loc
                        oc )
                    ocs |> Array.append (Array.create 1 oc)) |> Array.concat
                else
                    [||]

        { new DocumentSymbolProvider
          with
            member this.provideDocumentSymbols(doc, ct) =
                promise {
                    let! o = LanguageService.declarations doc.fileName
                    let data = mapRes doc o
                    return data |> ResizeArray
                } |> Case2
        }

    let activate selector (disposables: Disposable[]) =
        languages.registerDocumentSymbolProvider(selector, createProvider())
        |> ignore
        ()

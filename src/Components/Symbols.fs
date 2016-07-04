namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
 
open DTO
open Ionide.VSCode.Helpers

module Symbols =
    let private createProvider () =
        
        let convertToInt code =
            match code with
            | "C" -> 4      (*  CompletionItemKind.Class      *)
            | "E" -> 6      (*  CompletionItemKind.Enum       *)
            | "S" -> 6      (*  CompletionItemKind.Value      *)
            | "I" -> 10     (*  CompletionItemKind.Interface  *)
            | "N" -> 1      (*  CompletionItemKind.Module     *)
            | "M" -> 11     (*  CompletionItemKind.Method     *)
            | "P" -> 6      (*  CompletionItemKind.Property   *)
            | "F" -> 7      (*  CompletionItemKind.Field      *)
            | "T" -> 4      (*  CompletionItemKind.Class      *)
            | _   -> 0

        let mapRes (doc : TextDocument) o = 
             o.Data |> Array.map (fun syms ->
                let oc = createEmpty<SymbolInformation> 
                oc.name <- syms.Declaration.Name
                oc.kind <- syms.Declaration.GlyphChar |> convertToInt |> unbox
                oc.containerName <- syms.Declaration.Glyph
                let loc = createEmpty<Location>
                loc.range <-  Range
                            ( float syms.Declaration.BodyRange.StartLine   - 1.,
                                float syms.Declaration.BodyRange.StartColumn - 1.,
                                float syms.Declaration.BodyRange.EndLine     - 1.,
                                float syms.Declaration.BodyRange.EndColumn   - 1.)
                loc.uri <- Uri.file doc.fileName 
                oc.location <- loc
                let ocs =  syms.Nested |> Array.map (fun sym ->
                    let oc = createEmpty<SymbolInformation> 
                    oc.name <- sym.Name
                    oc.kind <- sym.GlyphChar |> convertToInt |> unbox
                    oc.containerName <- sym.Glyph
                    let loc = createEmpty<Location> 
                    loc.range <-  Range
                                ( float sym.BodyRange.StartLine   - 1.,
                                    float sym.BodyRange.StartColumn - 1.,
                                    float sym.BodyRange.EndLine     - 1.,
                                    float sym.BodyRange.EndColumn   - 1.)
                    loc.uri <- Uri.file doc.fileName
                    oc.location <- loc
                    oc )
                ocs |> Array.append (Array.create 1 oc)) |> Array.concat 


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

namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.languages

open DTO
open Ionide.VSCode.Helpers

[<ReflectedDefinition>]
module Outline =
    let private createProvider () =
        let provider = createEmpty<DocumentSymbolProvider > ()
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
            | _ -> 0

        provider.``provideDocumentSymbols <-`` (fun doc _ ->
            LanguageService.declarations doc.fileName
            |> Promise.success (fun (o : DeclarationResult) ->
                o.Data
                |> Array.map (fun s ->
                    let oc = createEmpty<SymbolInformation> ()
                    oc.name <- s.Declaration.Name
                    oc.kind <- s.Declaration.GlyphChar |> convertToInt |> unbox

                    let loc = createEmpty<Location> ()
                    loc.range <-  Range.Create(float s.Declaration.BodyRange.StartLine - 1.,
                                             float s.Declaration.BodyRange.StartColumn - 1.,
                                             float s.Declaration.BodyRange.EndLine - 1.,
                                             float s.Declaration.BodyRange.EndColumn - 1.)
                    loc.uri <- Uri.file doc.fileName
                    oc.location <- loc
                    let ocs =  s.Nested |> Array.map (fun s ->
                        let oc = createEmpty<SymbolInformation> ()
                        oc.name <- s.Name
                        oc.kind <- s.GlyphChar |> convertToInt |> unbox
                        let loc = createEmpty<Location> ()
                        loc.range <-  Range.Create(float s.BodyRange.StartLine - 1.,
                                                 float s.BodyRange.StartColumn - 1.,
                                                 float s.BodyRange.EndLine - 1.,
                                                 float s.BodyRange.EndColumn - 1.)
                        loc.uri <- Uri.file doc.fileName
                        oc.location <- loc
                        oc )
                    ocs |> Array.append (Array.create 1 oc))
                |> Array.concat) |> Promise.toThenable )
        provider

    let activate selector (disposables: Disposable[]) =
        Globals.registerDocumentSymbolProvider(selector, createProvider())
        |> ignore
        ()

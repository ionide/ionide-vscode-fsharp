module Ionide.VSCode.FSharp.LineLens

open System
open System.Collections.Generic
open Fable.Core
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open Fable.Core.JsInterop
open DTO
open LineLens2

type Number = float

let private logger =
    ConsoleAndOutputChannelLogger(Some "LineLens", Level.DEBUG, None, Some Level.DEBUG)

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module LineLensConfig =

    open System.Text.RegularExpressions

    type EnabledMode =
        | Never
        | ReplaceCodeLens
        | Always

    let private parseEnabledMode (s: string) =
        match s.ToLowerInvariant() with
        | "never" -> false
        | "always" -> true
        | "replacecodelens"
        | _ -> true


    let defaultConfig = { enabled = true; prefix = " //  " }

    let private themeRegex = Regex("\s*theme\((.+)\)\s*")

    let getConfig () =
        let cfg = workspace.getConfiguration ()

        let fsharpCodeLensConfig =
            cfg.get("[fsharp]", JsObject.empty).tryGet<bool> ("editor.codeLens")

        { enabled = cfg.get ("FSharp.lineLens.enabled", "replacecodelens") |> parseEnabledMode
          prefix = cfg.get ("FSharp.lineLens.prefix", defaultConfig.prefix) }


module DecorationUpdate =
    let formatSignature (sign: SignatureData) : string =
        let formatType =
            function
            | Contains "->" t -> sprintf "(%s)" t
            | t -> t

        let args =
            sign.Parameters
            |> List.map (fun group -> group |> List.map (fun p -> formatType p.Type) |> String.concat " * ")
            |> String.concat " -> "

        if String.IsNullOrEmpty args then
            sign.OutputType
        else
            args + " -> " + formatType sign.OutputType

    let interestingSymbolPositions (symbols: Symbols[]) : DTO.Range[] =
        symbols
        |> Array.collect (fun syms ->
            let interestingNested =
                syms.Nested
                |> Array.choose (fun sym ->
                    if
                        sym.GlyphChar <> "Fc"
                        && sym.GlyphChar <> "M"
                        && sym.GlyphChar <> "F"
                        && sym.GlyphChar <> "P"
                        || sym.IsAbstract
                        || sym.EnclosingEntity = "I" // interface
                        || sym.EnclosingEntity = "R" // record
                        || sym.EnclosingEntity = "D" // DU
                        || sym.EnclosingEntity = "En" // enum
                        || sym.EnclosingEntity = "E" // exception
                    then
                        None
                    else
                        Some sym.BodyRange)

            if syms.Declaration.GlyphChar <> "Fc" then
                interestingNested
            else
                interestingNested |> Array.append [| syms.Declaration.BodyRange |])

    let private lineRange (doc: TextDocument) (range: Vscode.Range) =
        let textLine = doc.lineAt range.start.line
        textLine.range

    let private getSignature (uri: Uri) (range: DTO.Range) =
        async {
            let! signaturesResult =
                LanguageService.signatureData uri range.StartLine (range.StartColumn - 1)
                |> Async.AwaitPromise

            return
                signaturesResult
                |> Option.map (fun r -> range |> CodeRange.fromDTO, formatSignature r.Data)
        }

    let signatureToDecoration
        (config: LineLens2.LineLensConfig)
        (doc: TextDocument)
        (range: Vscode.Range, signature: string)
        =
        LineLens2.LineLensDecorations.create "fsharp.linelens" (lineRange doc range) (config.prefix + signature)

    let private onePerLine (ranges: Range[]) =
        ranges
        |> Array.groupBy (fun r -> r.StartLine)
        |> Array.choose (fun (_, ranges) -> if ranges.Length = 1 then Some(ranges.[0]) else None)

    let private needUpdate (uri: Uri) (version: Number) { documents = documents } =
        (documents |> Documents.tryGetCachedAtVersion uri version).IsSome

    let declarationsResultToSignatures document declarationsResult uri =
        promise {
            let interesting = declarationsResult.Data |> interestingSymbolPositions

            let interesting = onePerLine interesting

            let! signatures =
                interesting
                |> Array.map (getSignature uri)
                |> Async.Sequential // Need to be sequential otherwise we'll flood the server with requests causing threapool exhaustion
                |> Async.StartAsPromise
                |> Promise.map (fun s -> s |> Array.choose (id))

            return signatures
        }


let private lineLensDecorationUpdate: LineLens2.DecorationUpdate =
    LineLens2.DecorationUpdate.updateDecorationsForDocument
        LanguageService.lineLenses
        DecorationUpdate.declarationsResultToSignatures
        DecorationUpdate.signatureToDecoration



let createLineLens () =
    LineLens2.LineLens("LineLens",lineLensDecorationUpdate, LineLensConfig.getConfig)

let Instance = createLineLens ()

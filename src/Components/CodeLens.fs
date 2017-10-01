namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Fable.Core.JsInterop
open DTO
open Ionide.VSCode.Helpers

module CodeLens =
    let refresh = EventEmitter<int>()
    let mutable private version = 0

    let formatSignature (sign : SignatureData) : string =
        let formatType = function
            | Contains "->" t -> sprintf "(%s)" t
            | t -> t

        let args =
            sign.Parameters
            |> List.map (fun group ->
                group
                |> List.map (fun p -> formatType p.Type)
                |> String.concat " * "
            )
            |> String.concat " -> "

        if String.IsNullOrEmpty args then sign.OutputType else args + " -> " + sign.OutputType

    let interestingSymbolPositions (symbols: Symbols[]): DTO.Range[] =
        symbols |> Array.collect(fun syms ->
            let interestingNested = syms.Nested |> Array.choose (fun sym ->
                if sym.GlyphChar <> "Fc"
                   && sym.GlyphChar <> "M"
                   && sym.GlyphChar <> "F"
                   && sym.GlyphChar <> "P"
                   || sym.IsAbstract
                   || sym.EnclosingEntity = "I"  // interface
                   || sym.EnclosingEntity = "R"  // record
                   || sym.EnclosingEntity = "D"  // DU
                   || sym.EnclosingEntity = "En" // enum
                   || sym.EnclosingEntity = "E"  // exception
                then None
                else Some sym.BodyRange)

            if syms.Declaration.GlyphChar <> "Fc" then
                interestingNested
            else
                interestingNested |> Array.append [|syms.Declaration.BodyRange|])

    let private createProvider () =
        let symbolsToCodeLens (doc : TextDocument) (symbols: Symbols[]) : CodeLens[] =
            interestingSymbolPositions symbols
                |> Array.map (CodeRange.fromDTO >> CodeLens)

        { new CodeLensProvider with
            member __.provideCodeLenses(doc, _) =
                promise {
                    let text = doc.getText()
                    let! result = LanguageService.declarations doc.fileName text version
                    let data =
                        if isNotNull result then
                            let res = symbolsToCodeLens doc result.Data
                            res
                        else [||]
                    return ResizeArray data
                }
                |> U2.Case2

            member __.resolveCodeLens(codeLens, _) =
                let load () =
                    promise {
                        let! signaturesResult =
                            LanguageService.signatureData
                                window.activeTextEditor.document.fileName
                                (int codeLens.range.start.line + 1)
                                (int codeLens.range.start.character + 1)
                        let cmd = createEmpty<Command>
                        cmd.title <- if isNotNull signaturesResult then formatSignature signaturesResult.Data else ""
                        codeLens.command <- cmd
                        return codeLens

                    }

                if int window.activeTextEditor.document.version > version then
                    Promise.create (fun resolve error ->
                        let mutable disp : Disposable option = None
                        let d = refresh.event.Invoke(unbox (fun n ->
                            resolve ()
                            disp |> Option.iter (fun n -> n.dispose () |> ignore)))
                        disp <- Some d

                    )
                    |> Promise.bind (fun _ ->
                        load ()
                    )
                    |> U2.Case2

                else
                    load () |> U2.Case2

            member __.onDidChangeCodeLenses = EventEmitter().event
        }

    let activate selector (context: ExtensionContext) =
        refresh.event.Invoke(fun n -> (version <- n ) |> unbox) |> context.subscriptions.Add
        languages.registerCodeLensProvider(selector, createProvider()) |> context.subscriptions.Add
        ()

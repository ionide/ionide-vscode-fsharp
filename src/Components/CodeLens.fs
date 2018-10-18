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
    let mutable private replacedByLineLens = false

    let formatSignature (sign : SignatureData) : string =
        let formatType =
            function
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

        if String.IsNullOrEmpty args then sign.OutputType else args + " -> " + formatType sign.OutputType

    let interestingSymbolPositions (symbols : Symbols[]) : DTO.Range[] =
        symbols |> Array.collect(fun syms ->
            let interestingNested =
                syms.Nested
                |> Array.choose (fun sym ->
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
        let symbolsToCodeLens (doc : TextDocument) (symbols : Symbols[]) : CodeLens[] =
            interestingSymbolPositions symbols
                |> Array.map (CodeRange.fromDTO >> CodeLens)

        { new CodeLensProvider with
            member __.provideCodeLenses(doc, _) =
                promise {
                    if replacedByLineLens then
                        return ResizeArray [||]
                    else
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

    let private createReferencesLensProvider () =
        let isActive () =
            let symbolCache = "FSharp.enableBackgroundSymbolCache" |> Configuration.get false
            let referencesCodeLenses = "FSharp.enableReferenceCodeLens" |> Configuration.get false
            symbolCache && referencesCodeLenses

        let symbolsToCodeLens (doc : TextDocument) (symbols : Symbols[]) : CodeLens[] =
            interestingSymbolPositions symbols
                |> Array.map (CodeRange.fromDTO >> CodeLens)

        { new CodeLensProvider with
            member __.provideCodeLenses(doc, _) =
                promise {
                    let isActive = isActive ()
                    if not isActive then
                        return ResizeArray [||]
                    else
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
                        let! (signaturesResult : SymbolUseResult) =
                            LanguageService.symbolUseProject
                                window.activeTextEditor.document.fileName
                                (int codeLens.range.start.line + 1)
                                (int codeLens.range.start.character + 1)
                        let cmd = createEmpty<Command>
                        cmd.title <-
                            if isNotNull signaturesResult then
                                if signaturesResult.Data.Uses.Length - 1 = 1 then "1 Reference"
                                elif signaturesResult.Data.Uses.Length = 0 then "0 References"
                                else sprintf "%d References" (signaturesResult.Data.Uses.Length - 1)
                            else ""
                        let locations =
                            signaturesResult.Data.Uses |> Array.map (fun f ->
                                let range =  CodeRange.fromSymbolUse f
                                let uri = vscode.Uri.file f.FileName
                                Location(uri, !!range)
                            ) |> Array.toSeq |> ResizeArray
                        let cmd =
                            if signaturesResult.Data.Uses.Length > 1 then
                                cmd.command <- "editor.action.showReferences"
                                cmd.arguments <- [!!vscode.Uri.file(window.activeTextEditor.document.fileName); !!codeLens.range.start; !!locations ] |> List.toSeq |> ResizeArray |> Some
                                cmd
                            else cmd

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

    let configChangedHandler () =
        let cfg = workspace.getConfiguration()
        replacedByLineLens <- (cfg.get("FSharp.lineLens.enabled", "never").ToLowerInvariant()) = "replacecodelens"


    let activate selector (context : ExtensionContext) =
        workspace.onDidChangeConfiguration $ (configChangedHandler, (), context.subscriptions) |> ignore
        refresh.event.Invoke(fun n -> (version <- n ) |> unbox) |> context.subscriptions.Add
        languages.registerCodeLensProvider(selector, createProvider()) |> context.subscriptions.Add
        languages.registerCodeLensProvider(selector, createReferencesLensProvider ()) |> context.subscriptions.Add

        configChangedHandler()
        ()
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
    let mutable cache: Map<string, CodeLens[]> = Map.empty

    let private createProvider () =
        let symbolsToCodeLens (doc : TextDocument) (symbols: Symbols[]) : CodeLens[] =
             symbols |> Array.collect (fun syms ->
                let range = CodeRange.fromDTO syms.Declaration.BodyRange
                let codeLens = CodeLens range

                let codeLenses =  syms.Nested |> Array.choose (fun sym ->
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
                    else Some (CodeLens (CodeRange.fromDTO sym.BodyRange)))

                if syms.Declaration.GlyphChar <> "Fc" then codeLenses
                else
                    codeLenses |> Array.append [|codeLens|])

        let formatSignature (sign : string) : string =
            let sign =
                match sign with
                | StartsWith "val" _
                | StartsWith "member" _
                | StartsWith "abstract" _
                | StartsWith "static" _
                | StartsWith "override" _ -> sign
                | _ ->
                    match sign.IndexOf "(" with
                    | i when i > 0 ->
                        sign.Substring(0, i) + ":" + sign.Substring(i+1)
                    | _ -> sign

            let sign = if sign.Contains ":" then sign.Split(':').[1 ..] |> String.concat ":" else sign
            let parms = sign.Split([|"->"|], StringSplitOptions.RemoveEmptyEntries)
            parms
            |> Seq.map (function
                | Contains "(requires" p -> p
                | Contains "*" p ->
                    p.Split '*' |> Seq.map (fun z -> if z.Contains ":" then z.Split(':').[1] else z) |> String.concat "* "
                | Contains ":" p ->
                    p.Split(':').[1]
                | p -> p)
            |> Seq.map String.trim
            |> String.concat " -> "

        { new CodeLensProvider with
            member __.provideCodeLenses(doc, _) =
                promise {
                    let! _ = LanguageService.parse doc.fileName (doc.getText()) doc.version
                    let! result = LanguageService.declarations doc.fileName
                    let data = symbolsToCodeLens doc result.Data
                    let d =
                        if data.Length > 0 then
                            cache <- cache |> Map.add doc.fileName data
                            data
                        else
                            defaultArg (cache |> Map.tryFind doc.fileName) [||]

                    return ResizeArray d
                } |> Case2


            member __.resolveCodeLens(codeLens, _) =
                promise {
                    let! signaturesResult =
                        LanguageService.signature
                            window.activeTextEditor.document.fileName
                            (int codeLens.range.start.line + 1)
                            (int codeLens.range.start.character + 1)

                    let cmd = createEmpty<Command>
                    cmd.title <- formatSignature signaturesResult.Data
                    codeLens.command <- cmd
                    return codeLens
                } |> Case2
        }

    let activate selector (disposables: Disposable[]) =
        languages.registerCodeLensProvider(selector, createProvider()) |> ignore
        ()

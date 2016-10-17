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
    let private createProvider () =
        let symbolsToCodeLens (doc : TextDocument) (symbols: Symbols[]) : CodeLens[] =
             symbols |> Array.collect (fun syms ->
                let range = CodeRange.fromDTO syms.Declaration.BodyRange
                let codeLens = CodeLens range

                let codeLenses =  syms.Nested |> Array.choose (fun sym ->
                    if sym.GlyphChar <> "Fc"
                       && sym.GlyphChar <> "M"
                       && sym.GlyphChar <> "F"
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

        let formatSingature (sign : string) : string =
            let sign =
                if sign.StartsWith "val" || sign.StartsWith "member" || sign.StartsWith "abstract" then
                    sign
                else
                    let i = sign.IndexOf("(")
                    if i > 0 then
                        sign.Substring(0, i) + ":" + sign.Substring(i+1)
                    else
                        sign

            let sign = if sign.Contains(":") then sign.Split(':').[1 ..] |> String.concat ":" else sign
            let parms = sign.Split( [|"->"|], StringSplitOptions.RemoveEmptyEntries)
            parms
            |> Seq.map (fun p ->
                if p.Contains "(requires" then
                    p
                elif p.Contains "*" then
                    p.Split '*' |> Seq.map (fun z -> if z.Contains ":" then z.Split(':').[1] else z) |> String.concat "* "
                elif p.Contains "," then
                    p.Split ',' |> Seq.map (fun z -> if z.Contains ":" then z.Split(':').[1] else z) |> String.concat "* "
                elif p.Contains ":" then
                    p.Split(':').[1]
                else p)
            |> Seq.map String.trim
            |> String.concat " -> "
            |> String.replace "<" "&lt;"
            |> String.replace ">" "&gt;"

        { new CodeLensProvider with
            member __.provideCodeLenses(doc, _) =
                promise {
                    let! _ = LanguageService.parse doc.fileName (doc.getText())
                    let! result = LanguageService.declarations doc.fileName
                    let data = symbolsToCodeLens doc result.Data
                    return ResizeArray data
                } |> Case2


            member __.resolveCodeLens(codeLens, _) =
                promise {
                    let! signaturesResult =
                        LanguageService.toolbar
                            window.activeTextEditor.document.fileName
                            (int codeLens.range.start.line + 1)
                            (int codeLens.range.start.character + 1)

                    let res =
                        signaturesResult.Data
                        |> Array.rev
                        |> Array.tryHead
                        |> Option.bind Array.tryHead
                        |> Option.map (fun x -> x.Signature)
                        |> Option.fill ""

                    let sign = res.Split('\n').[0] |> String.trim |> formatSingature
                    let cmd = createEmpty<Command>
                    cmd.title <- sprintf "%s" sign
                    codeLens.command <- cmd
                    return codeLens
                } |> Case2
        }

    let activate selector (disposables: Disposable[]) =
        languages.registerCodeLensProvider(selector, createProvider())
        |> ignore
        ()

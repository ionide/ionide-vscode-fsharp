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
        let mapRes (doc : TextDocument) o =
             o.Data |> Array.collect (fun syms ->
                let range = Range
                                ( float syms.Declaration.BodyRange.StartLine - 1.,
                                    float syms.Declaration.BodyRange.StartColumn - 1.,
                                    float syms.Declaration.BodyRange.EndLine - 1.,
                                    float syms.Declaration.BodyRange.EndColumn - 1.)
                let cl = CodeLens(range)

                let cls =  syms.Nested |> Array.choose (fun sym ->
                    if sym.GlyphChar <> "Fc" && sym.GlyphChar <> "M" then None
                    else
                    Range
                        ( float sym.BodyRange.StartLine - 1.,
                            float sym.BodyRange.StartColumn - 1.,
                            float sym.BodyRange.EndLine - 1.,
                            float sym.BodyRange.EndColumn - 1.)
                    |> CodeLens |> Some )
                if syms.Declaration.GlyphChar <> "Fc" then cls
                else
                    cls |> Array.append (Array.create 1 cl))

        let toSingature (t : string) =
            let t =
                if t.StartsWith "val" || t.StartsWith "member" then
                    t
                else
                    let i = t.IndexOf("(")
                    if i > 0 then
                        t.Substring(0, i) + ":" + t.Substring(i+1)
                    else
                        t

            let sign = if t.Contains(":") then t.Split(':').[1 ..] |> String.concat ":" else t
            let parms = sign.Split( [|"->"|], StringSplitOptions.RemoveEmptyEntries)
            let nSing =
                parms |> Seq.map (fun p ->
                    if p.Contains "(requires" then
                        p
                    elif p.Contains "*" then
                        p.Split('*') |> Seq.map (fun z -> if z.Contains ":" then z.Split(':').[1] else z) |> String.concat "* "
                    elif p.Contains "," then
                        p.Split(',') |> Seq.map (fun z -> if z.Contains ":" then z.Split(':').[1] else z) |> String.concat "* "
                    elif p.Contains ":" then
                        p.Split(':').[1]
                    else p
                ) |> String.concat "->"
            nSing.Replace("<", "&lt;").Replace(">", "&gt;")


        { new CodeLensProvider
          with
            member this.provideCodeLenses(doc, ct) =
                promise {
                    let! _ = LanguageService.parse doc.fileName (doc.getText())
                    let! o = LanguageService.declarations doc.fileName
                    let data = mapRes doc o
                    Browser.console.log("Provide", data)
                    return data |> ResizeArray
                } |> Case2

            member this.resolveCodeLens(cl, ct) =
                promise {
                    let! o = LanguageService.toolbar (window.activeTextEditor.document.fileName) (int cl.range.start.line + 1) (int cl.range.start.character + 1)
                    let res = (o.Data |> Array.fold (fun acc n -> (n |> Array.toList) @ acc ) []).Head.Signature
                    let sign = res.Split('\n').[0].Trim() |> toSingature
                    let cmd = createEmpty<Command>
                    cmd.title <- sprintf "%s" sign
                    cl.command <- cmd
                    Browser.console.log("Resolve", cl, res)
                    return cl
                } |> Case2
        }

    let activate selector (disposables: Disposable[]) =
        languages.registerCodeLensProvider(selector, createProvider())
        |> ignore
        ()


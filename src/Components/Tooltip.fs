namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers

module Tooltip =

    let private createProvider () =


        let mapResult (doc : TextDocument) (pos : Position) o =
            let range = doc.getWordRangeAtPosition pos
            if isNotNull o then
                let res = (o.Data |> Array.collect id).[0]
                if JS.isDefined res.Signature then
                    let markStr lang (value:string) : MarkedString =
                        createObj [
                            "language" ==> lang
                            "value" ==> value.Trim()
                        ] |> Case2
                    let sigContent =
                        res.Signature.Split('\n')
                        |> Array.filter(String.IsNullOrWhiteSpace>>not)
                        |> String.concat "\n"
                        |> markStr "fsharp"
                        |> Array.singleton
                    let commentContent =
                        res.Comment.Split('\n')
                        |> Array.filter(String.IsNullOrWhiteSpace>>not)
                        |> Array.mapi (fun i n ->
                            let v =
                                if i = 0 && not(String.IsNullOrWhiteSpace n)
                                then "\n" + n.Trim()
                                else n.Trim()
                            v)
                        |> fun n ->
                            if n.Length <> 0 then
                                n |> String.concat "\n\n" |> markStr "markdown" |> Array.singleton
                            else [||]
                    let result = createEmpty<Hover>
                    result.range <- range
                    result.contents <- Array.append sigContent commentContent |> ResizeArray
                    result
                else
                    createEmpty<Hover>
            else
                createEmpty<Hover>

        { new HoverProvider
          with
            member this.provideHover(doc, pos, _ ) =
                promise {
                    let! res = LanguageService.tooltip doc.fileName (int pos.line + 1) (int pos.character + 1)
                    return mapResult doc pos res
                } |> Case2

        }

    let activate selector (disposables: Disposable[]) =
        languages.registerHoverProvider(selector, createProvider())
        |> ignore
        ()

namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.vscode
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
                        ] |> U3.Case3

                    let fsharpBlock (lines: string[]) : MarkedString =
                        lines |> String.concat "\n" |> markStr "fsharp"

                    let sigContent =
                        let lines =
                            res.Signature
                            |> String.split [|'\n'|]
                            |> Array.filter (not << String.IsNullOrWhiteSpace)

                        match lines |> Array.splitAt (lines.Length - 1) with
                        | (h, [| StartsWith "Full name:" fullName |]) ->
                            [| yield fsharpBlock h
                               yield U3.Case1 (MarkdownString("*").appendText(fullName).appendMarkdown("*")) |]
                        | _ -> [| fsharpBlock lines |]

                    let commentContent =
                        res.Comment
                        |> Markdown.createCommentBlock
                        |> U3.Case1
                    let footerContent =
                        res.Footer
                        |> String.split [|'\n' |]
                        |> Array.filter (not << String.IsNullOrWhiteSpace)
                        |> Array.map (fun n -> U3.Case1 (MarkdownString("*").appendText(n).appendMarkdown("*")))

                    let result = createEmpty<Hover>
                    result.range <- range
                    result.contents <- Array.append sigContent [| yield commentContent; yield! footerContent |] |> ResizeArray
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
                } |> U2.Case2

        }

    let activate selector (context: ExtensionContext) =
        languages.registerHoverProvider(selector, createProvider())
        |> context.subscriptions.Add
        ()

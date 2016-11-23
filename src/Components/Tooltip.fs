namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open System.Text.RegularExpressions
open DTO
open Ionide.VSCode.Helpers

module Tooltip =
    let private createProvider () =
        let createCommentBlock (comment: string) : MarkedString[] =
            comment.Split '\n'
            |> Array.filter(String.IsNullOrWhiteSpace>>not)
            |> Array.mapi (fun i line ->
                let v =
                    if i = 0 && not (String.IsNullOrWhiteSpace line)
                    then "\n" + line.Trim()
                    else line.Trim()
                Markdown.replaceXml v
               )
            |> String.concat "\n\n"
            |> String.trim
            |> Case1
            |> Array.singleton

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
                               yield Case1 ("_" + fullName + "_") |]
                        | _ -> [| fsharpBlock lines |]

                    let commentContent =
                        res.Comment
                        |> String.replace "&lt;" "<"
                        |> String.replace "&gt;" ">"
                        |> createCommentBlock
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

namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open Ionide.VSCode.Helpers

open DTO
open Ionide.VSCode.Helpers

module CommentNavigation =


    let private createProvider () =

        { new DocumentLinkProvider
          with
            member this.provideDocumentLinks(doc, ct) =
                doc.getText()
                |> String.split [|'\n'|]
                |> Array.mapi (fun i r -> i,r)
                |> Array.where (snd>>String.replace " " "" >> String.startsWith "//<code:")
                |> Array.map (fun (i, l) ->
                    promise {
                        let res = String.split [|':'; '#' |] l
                        let startChar = l.IndexOf ":" |> float
                        let endChar = l.IndexOf ">" |> float
                        let dir = path.dirname doc.fileName
                        let path = path.join(dir, res.[1])
                        let location = res.[2] |> String.replace ">" ""
                        let! line =
                            if location |> float |> JS.isNaN |> not then
                                location |> Promise.lift
                            else
                                promise {
                                    let! td = workspace.openTextDocument path
                                    let! _ = LanguageService.parse td.fileName (td.getText()) td.version
                                    let! symbols = Symbols.getSymbols td
                                    let symOpt = symbols |> Array.tryFind (fun n -> n.name = location.Trim())
                                    return
                                        match symOpt with
                                        | None -> "1"
                                        | Some symbol ->
                                            (symbol.location.range.start.line + 1.).ToString()
                                }
                        let file = "file://" + path.Replace("\\", "/") + "#L" + line
                        let uri = Uri.parse file
                        let l = float i

                        let range = vscode.Range(l,startChar + 1.,l,endChar)
                        return DocumentLink(range,uri)
                    })
                |> Promise.all
                |> Case2
            }




    let activate selector (disposables: Disposable[]) =
        languages.registerDocumentLinkProvider (selector, createProvider()) |> ignore

        ()


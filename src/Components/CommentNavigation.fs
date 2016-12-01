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

    let mutable file : string option = None
    let mutable line : float option = None


    let private createProvider () =

        { new DocumentLinkProvider
          with
            member this.provideDocumentLinks(doc, ct) =
                doc.getText()
                |> String.split [|'\n'|]
                |> Array.mapi (fun i r -> i,r)
                |> Array.where (snd>>String.replace " " "" >> String.startsWith "//<code:")
                |> Array.map (fun (i,r) -> i, String.split [|':'; '#' |] r, r)
                |> Array.map (fun (i, res, l) ->

                    let startChar = l.IndexOf ":" |> float
                    let endChar = l.IndexOf ">" |> float
                    let dir = path.dirname doc.fileName
                    let path = path.join(dir, res.[1])
                    line <- res.[2] |> String.replace ">" "" |> float |> Some
                    file <- Some path
                    let uri = Uri.file path
                    let l = float i



                    let range = vscode.Range(l,startChar + 1.,l,endChar)
                    DocumentLink(range,uri))
                |> Case1
            }

    let navigate (n : TextEditor) =
        let f = defaultArg file ""
        let l = defaultArg line 0.
        if n.document.fileName = f then
            file <- None
            line <- None
            let range = vscode.Range(l,0.,l,100.)
            n.revealRange(range, TextEditorRevealType.InCenter )



        ()

    let activate selector (disposables: Disposable[]) =
        languages.registerDocumentLinkProvider (selector, createProvider()) |> ignore
        window.onDidChangeActiveTextEditor $ (navigate, (), disposables) |> ignore

        ()


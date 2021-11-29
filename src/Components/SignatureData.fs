namespace Ionide.VSCode.FSharp

open System
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open DTO

module SignatureData =

    let generateDoc () =
        let editor = window.activeTextEditor.Value
        let document = editor.document

        match document with
        | Document.FSharp
        | Document.FSharpScript ->
            let line = editor.selection.active.line
            let col = editor.selection.active.character

            LanguageService.generateDocumentation document.fileName (int line) (int col)
            |> Promise.bind (fun (p: SignatureDataResult) ->
                promise {
                    let pms =
                        p.Data.Parameters
                        |> Seq.concat
                        |> Seq.where (fun prm -> String.IsNullOrWhiteSpace prm.Name |> not)
                        |> Seq.map (fun prm -> sprintf """/// <param name="%s"></param>""" prm.Name)
                        |> String.concat "\n"

                    let generics =
                        p.Data.Generics
                        |> Seq.map (fun generic -> sprintf """/// <typeparam name="'%s"></typeparam>""" generic)
                        |> String.concat "\n"

                    let comment =
                        [ yield "/// <summary>"
                          yield "/// "
                          yield "/// </summary>"
                          if pms <> "" then yield pms
                          if generics <> "" then yield generics
                          yield "/// <returns></returns>" ]
                        |> String.concat "\n"

                    let x = editor.selection.active.line
                    let t = document.getText (vscode.Range.Create(x, 0., x, 1000.))
                    let t' = t.TrimStart(' ')
                    let spsC = t.Length - t'.Length
                    let sps = String.replicate spsC " "

                    let cmnt =
                        comment
                        |> String.split [| '\n' |]
                        |> Seq.map (fun n -> sprintf "%s%s" sps n)
                        |> String.concat "\n"

                    let edit = vscode.WorkspaceEdit.Create()
                    edit.insert (document.uri, vscode.Position.Create(x, 0.), cmnt + "\n")
                    return! workspace.applyEdit edit
                })
            |> ignore
        | _ -> ()


    let activate (context: ExtensionContext) =
        commands.registerCommand ("fsharp.generateDoc", generateDoc |> objfy2)
        |> context.Subscribe

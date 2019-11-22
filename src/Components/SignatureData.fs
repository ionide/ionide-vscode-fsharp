namespace Ionide.VSCode.FSharp

open System
open Fable.Import.vscode
open DTO
open Ionide.VSCode.Helpers

module SignatureData =

    let generateDoc () =
        let editor = window.activeTextEditor.document
        match editor with
        | Document.FSharp  ->
            let line = window.activeTextEditor.selection.active.line
            let col = window.activeTextEditor.selection.active.character
            LanguageService.generateDocumentation editor.fileName (int line) (int col)
            |> Promise.bind (fun (p : SignatureDataResult)  ->
                let pms =
                    p.Data.Parameters
                    |> Seq.concat
                    |> Seq.where (fun prm -> String.IsNullOrWhiteSpace prm.Name |> not)
                    |> Seq.map (fun prm -> sprintf """/// <param name="%s"></param>""" prm.Name)
                    |> String.concat "\n"

                let generics =
                    p.Data.Generics
                    |> Seq.map (fun generic ->
                        sprintf """/// <typeparam name="'%s"></typeparam>""" generic
                    )
                    |> String.concat "\n"

                let comment =
                    [
                        yield "/// <summary>"
                        yield "/// "
                        yield "/// </summary>"
                        if pms <> "" then
                            yield pms
                        if generics <> "" then
                            yield generics
                        yield "/// <returns></returns>"
                    ]
                    |> String.concat "\n"

                let x = window.activeTextEditor.selection.active.line
                let t = window.activeTextEditor.document.getText(Range(x, 0., x, 1000.))
                let t' = t.TrimStart(' ')
                let spsC = t.Length - t'.Length
                let sps = String.replicate spsC " "
                let cmnt = comment |> String.split [|'\n'|] |> Seq.map (fun n -> sprintf "%s%s" sps n ) |> String.concat "\n"
                let edit = WorkspaceEdit()
                edit.insert(window.activeTextEditor.document.uri, Position(x, 0.), cmnt + "\n" )
                workspace.applyEdit edit
            )
            |> ignore
        | _ ->
            ()


    let activate (context : ExtensionContext) =
        commands.registerCommand("fsharp.generateDoc", generateDoc |> unbox<Func<obj,obj>>) |> context.subscriptions.Add

        ()

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

module SignatureData =
    let generateDoc () =
        let editor = window.activeTextEditor.document
        match editor with
        | Document.FSharp  ->
            let line = window.activeTextEditor.selection.active.line + 1.
            let col = window.activeTextEditor.selection.active.character + 1.
            LanguageService.signatureData editor.fileName (int line) (int col)
            |> Promise.bind (fun (p : SignatureDataResult)  ->
                let pms =
                    p.Data.Parameters
                    |> Seq.collect id
                    |> Seq.where (fun prm -> String.IsNullOrWhiteSpace prm.Name |> not )
                    |> Seq.map (fun prm -> sprintf "///  * `%s` - parameter of type `%s`" prm.Name prm.Type)
                    |> String.concat "\n"
                let pms = if pms = "" then "///" else pms
                let comment = sprintf "\n///**Description**\n///\n///**Parameters**\n%s\n///\n///**Output Type**\n///  * `%s`\n///\n///**Exceptions**\n///" pms p.Data.OutputType
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


    let activate (context: ExtensionContext) =
        commands.registerCommand("fsharp.generateDoc", generateDoc |> unbox<Func<obj,obj>>) |> context.subscriptions.Add

        ()
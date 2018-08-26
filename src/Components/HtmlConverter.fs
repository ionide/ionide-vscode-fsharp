namespace Ionide.VSCode.FSharp

open System
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Ionide.VSCode.Helpers
open HtmlConverter.Converter

module HtmlConverter =

    let convert () =
        let editor = vscode.window.activeTextEditor
        let selectedText = editor.document.getText(editor.selection)

        if selectedText.Length = 0 then
            vscode.window.showWarningMessage("No selection found, please select some HTML text and try again.")
            // Map the callback returned to false
            |> Promise.map (fun _ -> false)
        else
            // Return the result of the edit promise, otherwise VSCode output an Error message saying the command failed
            editor.edit(fun builder ->
                builder.replace(!^editor.selection, htmlToElmish (selectedText))
            )


    let activate (context : ExtensionContext) =
        commands.registerCommand("fsharp.htmlConverter.convert", convert |> unbox<Func<obj,obj>>)
        |> context.subscriptions.Add

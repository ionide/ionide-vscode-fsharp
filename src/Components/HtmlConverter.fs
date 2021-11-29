namespace Ionide.VSCode.FSharp

open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open HtmlConverter.Converter

module HtmlConverter =

    let convert () =
        let editor = window.activeTextEditor.Value
        let selectedText = editor.document.getText (editor.selection)

        if selectedText.Length = 0 then
            window.showWarningMessage ("No selection found, please select some HTML text and try again.", null)
            |> Promise.ofThenable
            // Map the callback returned to false
            |> Promise.map (fun _ -> false)
        else
            // Return the result of the edit promise, otherwise VSCode output an Error message saying the command failed
            editor.edit (fun builder -> builder.replace (!^editor.selection, htmlToElmish (selectedText)))
            |> Promise.ofThenable


    let activate (context: ExtensionContext) =
        commands.registerCommand ("fsharp.htmlConverter.convert", convert |> objfy2)
        |> context.Subscribe

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

            LanguageService.generateDocumentation (document.uri, document.version) (int line, int col)
        | _ -> Promise.lift ()


    let activate (context: ExtensionContext) =
        commands.registerCommand ("fsharp.generateDoc", generateDoc |> objfy2)
        |> context.Subscribe

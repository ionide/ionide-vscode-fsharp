namespace Ionide.VSCode.FSharp

open System
open Fable.Import.vscode
open DTO

module SignatureData =

    let generateDoc () =
        let editor = window.activeTextEditor.document
        match editor with
        | Document.FSharp  ->
            let line = window.activeTextEditor.selection.active.line
            let col = window.activeTextEditor.selection.active.character
            let args : LanguageService.Types.VersionedTextDocumentPositionParams = {
                TextDocument = { Uri = LanguageService.handleUntitled (editor.uri.ToString())
                                 Version = Some (int editor.version) }
                Position = { Line = int line
                             Character = int col }
            }
            commands.executeCommand("fsharp.generateXmlDoc", args)
            |> ignore
        | _ ->
            ()

    let activate (context : ExtensionContext) =
        commands.registerCommand("fsharp.generateDoc", generateDoc |> objfy2)
        |> context.subscriptions.Add

        ()

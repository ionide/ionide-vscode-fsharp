namespace Ionide.VSCode.FSharp

open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode

open DTO

module Help =

    let getHelp () =
        let te = window.activeTextEditor.Value
        let doc = te.document
        let pos = te.selection.start

        promise {
            let! res = LanguageService.f1Help (doc.uri) (int pos.line) (int pos.character)

            match res with
            | None -> return ()
            | Some res ->
                let api = res.Data.Replace("#ctor", "-ctor")

                let uri =
                    vscode.Uri.parse (sprintf "https://docs.microsoft.com/en-us/dotnet/api/%s" api)

                return! commands.executeCommand ("vscode.open", Some(box uri))
        }
        |> ignore


    let activate (context: ExtensionContext) =
        let registerCommand com (f: unit -> _) =
            commands.registerCommand (com, f |> objfy2) |> context.Subscribe

        registerCommand "fsharp.getHelp" getHelp

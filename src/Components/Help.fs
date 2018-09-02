namespace Ionide.VSCode.FSharp

open System
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode

open DTO
open Ionide.VSCode.Helpers

module Help =

    let getHelp () =
        let te = window.activeTextEditor
        let doc = te.document
        let pos = te.selection.start

        promise {
            let! res = LanguageService.f1Help (doc.fileName) (int pos.line + 1) (int pos.character + 1)
            let uri = Uri.parse "https://msdn.microsoft.com/query/dev15.query"
            let query = res.Data |> JS.encodeURIComponent |> sprintf "appId=Dev15IDEF1&l=EN-US&k=k(%s);k(DevLang-FSharp)&rd=true"
            let change =
                createObj [
                    "query" ==> query
                ]

            let uri' = uri?``with``(change)

            return! commands.executeCommand("vscode.open", uri', 3)
        } |> ignore


    let activate (context : ExtensionContext) =
        let registerCommand com (f : unit -> _) =
            vscode.commands.registerCommand(com, unbox<Func<obj,obj>> f)
            |> context.subscriptions.Add

        registerCommand "fsharp.getHelp" getHelp

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


module Help =
    let getHelp () =
        let te = window.activeTextEditor
        let doc = te.document
        let pos = te.selection.start

        promise {
            let! res = LanguageService.f1Help (doc.fileName) (int pos.line + 1) (int pos.character + 1)
            let uri = Uri.parse "https://msdn.microsoft.com/query/dev14.query"
            let query = res.Data |> JS.encodeURIComponent |> sprintf "appId=Dev14IDEF1&l=EN-US&k=k(%s);k(DevLang-fsharp);k(TargetFrameworkMoniker-.NETFramework,Version%%3Dv4.5)&rd=true"
            let change =
                createObj [
                    "query" ==> query
                ]

            let uri' = uri?``with``(change)

            return! commands.executeCommand("vscode.open", uri',3)
        } |> ignore


    let activate (disposables: Disposable[]) =
        let registerCommand com (f: unit-> _) =
            vscode.commands.registerCommand(com, unbox<Func<obj,obj>> f)
            |> ignore

        registerCommand "FSharp.getHelp" getHelp

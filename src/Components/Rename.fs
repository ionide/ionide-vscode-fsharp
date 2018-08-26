namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers

module Rename =

    let private createProvider () =
        let mapResult (doc : TextDocument) (newName : string) (o : SymbolUseResult) =
            let res = WorkspaceEdit ()
            if isNotNull o then
                o.Data.Uses |> Array.iter (fun s ->
                    let range = CodeRange.fromSymbolUse s
                    let te = TextEdit.replace(range, newName)
                    let uri = Uri.file s.FileName
                    res.replace(uri,range, newName))
            res

        { new RenameProvider
          with
            member this.provideRenameEdits(doc, pos, newName, _ ) =
                promise {
                    let! res = LanguageService.symbolUseProject (doc.fileName) (int pos.line + 1) (int pos.character + 1)
                    return mapResult doc newName res
                } |> U2.Case2}


    let activate selector (context : ExtensionContext) =
        languages.registerRenameProvider(selector, createProvider())
        |> context.subscriptions.Add
        ()

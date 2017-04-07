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
                    let range = Range(float s.StartLine - 1., (float s.EndColumn) - (float o.Data.Name.Length) - 1., float s.EndLine - 1., float s.EndColumn - 1.)
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
                } |> Case2}

    let activate selector (disposables: Disposable[]) =
        languages.registerRenameProvider(selector, createProvider())
        |> ignore
        ()

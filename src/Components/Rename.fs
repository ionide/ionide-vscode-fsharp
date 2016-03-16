namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.languages

open DTO
open Ionide.VSCode.Helpers

[<ReflectedDefinition>]
module Rename = 
    let private createProvider () =
        let provider = createEmpty<RenameProvider> ()
 
        let mapResult (doc : TextDocument) (newName : string) (o : SymbolUseResult) =  
            let res = WorkspaceEdit.Create () 
            
        
            o.Data.Uses |> Array.iter (fun s ->
                let range = Range.Create(float s.StartLine - 1., (float s.EndColumn) - (float o.Data.Name.Length) - 1., float s.EndLine - 1., float s.EndColumn - 1.)
                let te = TextEdit.replace(range, newName)
                let uri = Uri.file s.FileName
                res.replace(uri,range, newName)) 
            res 

        provider.``provideRenameEdits <-``(fun doc pos newName _ ->
            LanguageService.symbolUseProject (doc.fileName) (int pos.line + 1) (int pos.character + 1)
            |> Promise.success (mapResult doc newName)
            |> Promise.toThenable )
        provider

    let activate selector (disposables: Disposable[]) =
        Globals.registerRenameProvider(selector, createProvider())
        |> ignore
        ()

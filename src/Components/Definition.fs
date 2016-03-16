namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.languages

open DTO
open Ionide.VSCode.Helpers

[<ReflectedDefinition>]
module Definition =
    let private createProvider () =
        let provider = createEmpty<DefinitionProvider > ()

        let mapResult (doc : TextDocument) (pos : Position) (o : FindDeclarationResult) =
            let loc = createEmpty<Location> ()
            let range = doc.getWordRangeAtPosition pos
            let length = range._end.character - range.start.character            
            loc.uri <- Uri.file o.Data.File
            loc.range <- Range.Create(float o.Data.Line - 1., float o.Data.Column - 1., float o.Data.Line - 1., float o.Data.Column + length  - 1.)
            [| loc |]

        provider.``provideDefinition <-`` (fun doc pos _ ->
            LanguageService.findDeclaration (doc.fileName) (int pos.line + 1) (int pos.character + 1)
            |> Promise.success (mapResult doc pos)
            |> Promise.toThenable )
        provider

    let activate selector (disposables: Disposable[]) =
        Globals.registerDefinitionProvider(selector, createProvider())
        |> ignore

        ()

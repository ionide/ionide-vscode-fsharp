namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.languages

open DTO
open Ionide.VSCode.Helpers

[<ReflectedDefinition>]
module Reference =
    let private createProvider () =
        let provider = createEmpty<ReferenceProvider> ()

        let mapResult (doc : TextDocument) (o : SymbolUseResult) =  
            o.Data.Uses
            |> Array.map (fun s ->
                let res = createEmpty<Location> ()
                res.range <-  Range.Create(float s.StartLine - 1., float s.StartColumn - 1., float s.EndLine - 1., float s.EndColumn - 1.)
                res.uri <- Uri.file doc.fileName
                res  )

        provider.``provideReferences <-`` (fun doc pos _ _ ->
            LanguageService.symbolUse (doc.fileName) (int pos.line + 1) (int pos.character + 1)
            |> Promise.success (mapResult doc)
            |> Promise.toThenable )
        provider

    let activate selector (disposables: Disposable[]) =
        Globals.registerReferenceProvider(selector, createProvider())
        |> ignore
        ()

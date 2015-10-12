namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.Modes

open DTO

[<ReflectedDefinition>]
module Occurrences =
    let private createProvider () =
        let provider = createEmpty<IOccurrencesSupport > ()
        provider.``findOccurrences <-`` (fun doc pos _ ->
            LanguageService.symbolUse (doc.getPath ()) (int pos.line) (int pos.character)
            |> Promise.success (fun (o : SymbolUseResult) ->
                o.Data.Uses
                |> Array.map (fun s ->
                    let oc = createEmpty<IOccurrence> ()
                    oc.range <- Range.Create(float s.StartLine, float s.StartColumn, float s.EndLine, float s.EndColumn)
                    oc  )
                )
            |> Promise.toThenable )
        provider

    let activate (disposables: Disposable[]) =
        Globals.OccurrencesSupport.register("fsharp", createProvider())
        |> ignore
        ()

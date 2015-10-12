namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.Modes

open DTO

[<ReflectedDefinition>]
module Declaration =
    let private createProvider () =
        let provider = createEmpty<IDeclarationSupport > ()
        provider.``findDeclaration <-`` (fun doc pos _ ->
            LanguageService.findDeclaration (doc.getPath ()) (int pos.line) (int pos.character)
            |> Promise.success (fun (o : FindDeclarationResult) ->
                let res = createEmpty<IReference> ()
                let range = doc.getWordRangeAtPosition pos
                let length = range._end.character - range.start.character
                res.resource <- Uri.file o.Data.File
                res.range <- Range.Create(float o.Data.Line, float o.Data.Column, float o.Data.Line, float o.Data.Column + length )
                res )
            |> Promise.toThenable )
        provider

    let activate (disposables: Disposable[]) =
        Globals.DeclarationSupport.register("fsharp", createProvider())
        |> ignore

        ()

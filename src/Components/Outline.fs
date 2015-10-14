namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.Modes

open DTO

[<ReflectedDefinition>]
module Outline =
    let private createProvider () =
        let provider = createEmpty<IOutlineSupport > ()
        provider.``getOutline <-`` (fun doc _ ->
            LanguageService.declarations (doc.getPath ())
            |> Promise.success (fun (o : DeclarationResult) ->
                o.Data
                |> Array.map (fun s ->
                    let oc = createEmpty<IOutlineEntry> ()
                    oc.label <- s.Declaration.Name
                    oc._type <- s.Declaration.Glyph
                    oc.range <- Range.Create(float s.Declaration.BodyRange.StartLine,
                                             float s.Declaration.BodyRange.StartColumn,
                                             float s.Declaration.BodyRange.EndLine,
                                             float s.Declaration.BodyRange.EndColumn)
                    oc.children <- s.Nested |> Array.map (fun s ->
                        let oc = createEmpty<IOutlineEntry> ()
                        oc.label <- s.Name
                        oc._type <- s.Glyph
                        oc.range <- Range.Create(float s.BodyRange.StartLine,
                                                 float s.BodyRange.StartColumn,
                                                 float s.BodyRange.EndLine,
                                                 float s.BodyRange.EndColumn)
                        oc )
                    oc  )
                )
            |> Promise.toThenable )
        provider

    let activate (disposables: Disposable[]) =
        Globals.OutlineSupport.register("fsharp", createProvider())
        |> ignore
        ()

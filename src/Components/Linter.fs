namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode

open DTO

[<ReflectedDefinition>]
module Linter =
    let mutable private currentDiagnostic : Disposable option = None

    let private parse path text =
        LanguageService.parse path text
        |> Promise.success (fun (ev : ParseResult) ->
            currentDiagnostic |> Option.iter (fun cd -> cd.dispose () |> ignore)
            let diag =
                ev.Data
                |> Seq.distinctBy (fun d -> d.Severity, d.StartLine, d.StartColumn)
                |> Seq.map (fun d ->
                    let range = Range.Create(float d.StartLine, float d.StartColumn, float d.EndLine, float d.EndColumn)
                    let loc = Location.Create (Uri.file d.FileName, range)
                    let severity = if d.Severity = "Error" then 2 else 1
                    Diagnostic.Create(unbox severity, loc, d.Message) )
                |> Seq.toArray
                |> languages.Globals.addDiagnostics
            currentDiagnostic <- Some diag )
        |> ignore

    let private handler (event : TextDocumentChangeEvent) =
        parse (event.document.getPath ()) (event.document.getText ())


    let private handlerOpen (event : TextDocument) =
        parse (event.getPath ()) (event.getText ())
        //TODO: Find project and prase

    let activate (disposables: Disposable[]) =
        workspace.Globals.onDidChangeTextDocument
        |> EventHandler.add handler () disposables

        workspace.Globals.onDidOpenTextDocument
        |> EventHandler.add handlerOpen () disposables

        ()

namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode

open DTO
open Events

[<ReflectedDefinition>]
module Linter =
    let private handler (event : TextDocumentChangeEvent) =
        LanguageService.parse (event.document.getPath ()) (event.document.getText ())
        ()

    let mutable currentDiagnostic : Disposable option = None

    let activate (disposables: Disposable[]) =
        workspace.Globals.onDidChangeTextDocument
        |> EventHandler.add handler () disposables

        ParseEvent.Publish
        |> Observable.add (fun ev ->
            currentDiagnostic |> Option.iter (fun cd -> cd.dispose () |> ignore)
            let diag =
                ev.Data
                |> Seq.distinctBy (fun d -> d.Severity, d.StartLine, d.StartColumn)
                |> Seq.map (fun d ->
                    let range = Range.Create(float d.StartLine, float d.StartColumn, float d.EndLine, float d.EndColumn)
                    let loc = Location.Create (Uri.file d.FileName, range)
                    let severity =
                        if d.Severity = "Error" then 2 else 1
                        |> unbox<DiagnosticSeverity>
                    Diagnostic.Create(severity, loc, d.Message) )
                |> Seq.toArray
                |> languages.Globals.addDiagnostics
            currentDiagnostic <- Some diag
            () )

        ()

namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.languages
open FunScript.TypeScript.path
open FunScript.TypeScript.fs

open DTO 
open Ionide.VSCode.Helpers

[<ReflectedDefinition>]
module Linter =
    type DiagnosticCollection with
        [<JSEmitInline("{0}.set({1},{2})")>]
        member __.set'(uri : Uri, diagnostics : Diagnostic[]) : unit = failwith "never"


    let mutable private currentDiagnostic = Globals.createDiagnosticCollection ()    

    let private parse path text =

        let mapResult (ev : ParseResult) =
            ev.Data
            |> Seq.distinctBy (fun d -> d.Severity, d.StartLine, d.StartColumn)
            |> Seq.map (fun d ->
                try
                    let range = Range.Create(float d.StartLine - 1., float d.StartColumn - 1., float d.EndLine - 1., float d.EndColumn - 1.)
                    let loc = Location.Create (Uri.file d.FileName, range)
                    let severity = if d.Severity = "Error" then 0 else 1
                    Diagnostic.Create(range, d.Message, unbox severity) |> Some
                with
                | _ -> None )
            |> Seq.choose id
            |> Seq.toArray

        LanguageService.parse path text
        |> Promise.success (fun (ev : ParseResult) ->  (Uri.file path, mapResult ev) |> currentDiagnostic.set'  )


    let parseFile (file : TextDocument) =
        if file.languageId = "fsharp" then
            let path = file.fileName
            let prom = Project.find path
            match prom with
            | Some p -> p
                        |> LanguageService.project
                        |> Promise.bind (fun _ -> parse path (file.getText ()))
            | None -> parse path (file.getText ())  
        else
            Promise.lift (null |> unbox)  
            

    let mutable private timer = None : NodeJS.Timer option

    let private handler (event : TextDocumentChangeEvent) =
        timer |> Option.iter(Globals.clearTimeout)
        timer <- Some (Globals.setTimeout((fun _ -> 
            if event.document.languageId = "fsharp" then
                parse (event.document.fileName) (event.document.getText ()) |> ignore), 500.) )


    let private handlerOpen (event : TextEditor) =
        parseFile event.document

    let activate (disposables: Disposable[]) =
        workspace.Globals.onDidChangeTextDocument
        |> EventHandler.add handler () disposables

        window.Globals.onDidChangeActiveTextEditor
        |> EventHandler.add handlerOpen () disposables

        match window.Globals.visibleTextEditors |> Array.toList with
        | [] -> Promise.lift (null |> unbox)  
        | [x] -> parseFile x.document 
        | x::tail ->
            tail 
            |> List.fold (fun acc e -> acc |> Promise.bind(fun _ -> parseFile e.document ) )
               (parseFile x.document )
        |> ignore

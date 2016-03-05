namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.languages
open FunScript.TypeScript.path
open FunScript.TypeScript.fs
open System.Net

open DTO 
open Ionide.VSCode.Helpers

[<ReflectedDefinition>]
module Linter =
    type DiagnosticCollection with
        [<JSEmitInline("{0}.set({1},{2})")>]
        member __.set'(uri : Uri, diagnostics : Diagnostic[]) : unit = failwith "never"


    let mutable private currentDiagnostic = Globals.createDiagnosticCollection ()

    let private project p =
        let rec findFsProj dir =
            if Globals.lstatSync(dir).isDirectory() then
                let files = Globals.readdirSync dir
                let projfile = files |> Array.tryFind(fun s -> s.EndsWith(".fsproj"))
                match projfile with
                | None ->
                    let parent = if dir.LastIndexOf(Globals.sep) > 0 then dir.Substring(0, dir.LastIndexOf Globals.sep) else ""
                    if System.String.IsNullOrEmpty parent then None else findFsProj parent
                | Some p -> dir + Globals.sep + p |> Some
            else None

        p
        |> Globals.dirname
        |> findFsProj

    let private parse path text =

        let mapResult (ev : ParseResult) =
            ev.Data
            |> Seq.distinctBy (fun d -> d.Severity, d.StartLine, d.StartColumn)
            |> Seq.map (fun d ->
                let range = Range.Create(float d.StartLine - 1., float d.StartColumn - 1., float d.EndLine - 1., float d.EndColumn - 1.)
                let loc = Location.Create (Uri.file d.FileName, range)
                let severity = if d.Severity = "Error" then 0 else 1
                Diagnostic.Create(range, d.Message, unbox severity) )
            |> Seq.toArray

        LanguageService.parse path text
        |> Promise.success (fun (ev : ParseResult) ->  (Uri.file path, mapResult ev) |> currentDiagnostic.set'  )
        |> ignore

    let parseFile (file : TextDocument) =
        let path = file.fileName |> WebUtility.UrlEncode
        let prom = project path
        match prom with
        | Some p -> p
                    |> LanguageService.project
                    |> Promise.success (fun _ -> parse path (file.getText ()))
                    |> ignore
        | None -> parse path (file.getText ())

    let mutable private timer = None : NodeJS.Timer option

    let private handler (event : TextDocumentChangeEvent) =
        timer |> Option.iter(Globals.clearTimeout)
        timer <- Some (Globals.setTimeout((fun n -> parse (event.document.fileName) (event.document.getText ())), 500.) )


    let private handlerOpen (event : TextEditor) =
        parseFile event.document

    let activate (disposables: Disposable[]) =
        workspace.Globals.onDidChangeTextDocument
        |> EventHandler.add handler () disposables

        window.Globals.onDidChangeActiveTextEditor
        |> EventHandler.add handlerOpen () disposables

        let editor = window.Globals.activeTextEditor
        if JS.isDefined editor then parseFile editor.document

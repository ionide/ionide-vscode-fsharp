namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.path
open FunScript.TypeScript.fs

open DTO

[<ReflectedDefinition>]
module Linter =
    let mutable private currentDiagnostic : Disposable option = None

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
        |> Option.map (fun n -> LanguageService.project n )

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

    let parseFile (file : TextDocument) =
        let path = file.getPath ()
        let prom = project path
        match prom with
        | Some p -> p |> Promise.success (fun _ -> parse path (file.getText ())) |> ignore
        | None -> parse path (file.getText ())


    let private handler (event : TextDocumentChangeEvent) =
        parse (event.document.getPath ()) (event.document.getText ())

    let private handlerOpen (event : TextEditor) =
        parseFile <| event.getTextDocument ()

    let activate (disposables: Disposable[]) =
        workspace.Globals.onDidChangeTextDocument
        |> EventHandler.add handler () disposables

        window.Globals.onDidChangeActiveTextEditor
        |> EventHandler.add handlerOpen () disposables

        parseFile <| window.Globals.getActiveTextEditor().getTextDocument ()

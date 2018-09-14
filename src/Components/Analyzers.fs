namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node


open DTO
open Ionide.VSCode.Helpers
module node = Fable.Import.Node.Exports

module Analyzers =

    let mutable private currentDiagnostic = languages.createDiagnosticCollection ()
    let private fixes = ResizeArray<Fix>()

    let deleteDiagnostic uri = currentDiagnostic.delete uri

    let private diagnosticFromAnalyzerMessage file (msg : AnalyzerMsg) =
        let range = CodeRange.fromDTO msg.Range
        let s =
            match msg.Severity with
            | "info" -> DiagnosticSeverity.Information
            | "warning" -> DiagnosticSeverity.Warning
            | "error" -> DiagnosticSeverity.Error
            | _ -> DiagnosticSeverity.Hint
        Diagnostic(range, msg.Type + ": " + msg.Message, s), file


    let private createProvider () =
        { new CodeActionProvider
          with
            member __.provideCodeActions(doc, range, context, ct) =
                let diagnostics = context.diagnostics
                let diagnostic = diagnostics |> Seq.tryFind (fun d -> currentDiagnostic.get doc.uri |> Seq.contains d)
                let res =
                    match diagnostic with
                    | None -> [||]
                    | Some d ->
                        fixes
                        |> Seq.where (fun f ->  (unbox f.FromRange.StartColumn ) = d.range.start.character + 1. &&
                                                (unbox f.FromRange.EndColumn ) = d.range.``end`` .character + 1. &&
                                                (unbox f.FromRange.StartLine ) = d.range.start.line + 1. &&
                                                (unbox f.FromRange.EndLine ) = d.range.``end`` .line + 1.)
                        |> Seq.map (fun suggestion ->
                            let cmd = createEmpty<Command>
                            cmd.title <- sprintf "Replace with %s" suggestion.ToText
                            cmd.command <- "fsharp.analyzerFix"
                            cmd.arguments <- Some ([| doc |> unbox; d.range |> unbox; suggestion.ToText |> unbox; |] |> ResizeArray)
                            cmd)
                        |> Seq.toArray
                res |> ResizeArray |> U2.Case1
            }

    let private applyQuickFix(doc : TextDocument, range : vscode.Range, suggestion : string) =
        let edit = WorkspaceEdit()
        let uri = Uri.file doc.fileName
        edit.replace(uri, range, suggestion)
        workspace.applyEdit edit
        //|> Promise.onSuccess (fun _ -> lintDocument doc.fileName) //TODO

    let private mapResult file (ev : AnalyzerResult) =
        if isNotNull ev then
            ev.Data.Messages
            |> Seq.map (diagnosticFromAnalyzerMessage file)
            |> ResizeArray
        else
            ResizeArray ()

    let handler (res : AnalyzerResult) =
        if isNotNull res then
            res.Data.Messages |> Array.where (fun a -> isNotNull a.Fixes) |> Array.collect (fun a ->a.Fixes) |> fixes.AddRange
            (Uri.file res.Data.File, mapResult path res |> Seq.map fst |> ResizeArray) |> currentDiagnostic.set

    let activate selector (context : ExtensionContext) =
        let analyzerPaths = "FSharp.analyzersPath" |> Configuration.get [| "packages/Analyzers"; "analyzers" |]
        let p = node.path.join(workspace.rootPath, "analyzers") //TODO: configure set of paths for loading analyzers
        analyzerPaths
        |> Array.iter (fun p ->
            let p = node.path.join(workspace.rootPath, p)
            LanguageService.registerAnalyzer p
            |> ignore
        )
        LanguageService.registerNotifyAnalyzer handler

        languages.registerCodeActionsProvider (selector, createProvider()) |> context.subscriptions.Add
        commands.registerCommand("fsharp.analyzerFix",Func<obj,obj,obj,obj>(fun a b c -> applyQuickFix(a |> unbox, b |> unbox, c |> unbox) |> unbox )) |> context.subscriptions.Add

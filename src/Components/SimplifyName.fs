namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers

module SimplifyName =
    let mutable private currentDiagnostic = languages.createDiagnosticCollection ()
    let refresh = EventEmitter<string>()
    let private isAnalyzerEnabled () = "FSharp.simplifyNameAnalyzer" |> Configuration.get true

    let private diagnosticFromRange file (data : SimplifiedNameData) =
        let range = CodeRange.fromSimplifiedNameRange data.UnnecessaryRange
        Diagnostic(range, "This qualifier is redundant", DiagnosticSeverity.Information), file

    let private mapResult file (ev : SimplifiedNameResult) =
        if isNotNull ev then
            ev.Data.Names
            |> Seq.map (diagnosticFromRange file)
            |> ResizeArray
        else
            ResizeArray ()

    let private analyzeDocument path =
        if isAnalyzerEnabled () then
            LanguageService.simplifiedNames path
            |> Promise.onSuccess (fun (ev : SimplifiedNameResult) ->
                if isNotNull ev then
                    (Uri.file path, mapResult path ev |> Seq.map fst |> ResizeArray) |> currentDiagnostic.set)
            |> ignore

    let private handler filename =
        analyzeDocument filename

    let private createProvider () =

        { new CodeActionProvider
          with
            member __.provideCodeActions(doc, range, context, ct) =
                let diagnostics = context.diagnostics
                let diagnostic = diagnostics |> Seq.tryFind (fun d -> d.message.Contains "This qualifier is redundant")
                let res =
                    match diagnostic with
                    | None -> [||]
                    | Some d ->
                        let cmd = createEmpty<Command>
                        cmd.title <- "Remove redundant qualifier"
                        cmd.command <- "fsharp.simplifyNameFix"

                        cmd.arguments <- Some ([| doc |> unbox; d.range |> unbox; |] |> ResizeArray)
                        [|cmd |]
                res |> ResizeArray |> U2.Case1
            }

    let private applyQuickFix(doc : TextDocument, range : vscode.Range, suggestion : string) =
        let edit = WorkspaceEdit()
        let uri = Uri.file doc.fileName
        edit.replace(uri, range, suggestion)
        workspace.applyEdit edit
        |> Promise.onSuccess (fun _ -> analyzeDocument doc.fileName)

    let activate selector (context: ExtensionContext) =
        refresh.event $ (handler,(), context.subscriptions) |> ignore
        if JS.isDefined window.activeTextEditor then
            match window.activeTextEditor.document with
            | Document.FSharp -> refresh.fire window.activeTextEditor.document.fileName
            | _ -> ()

        languages.registerCodeActionsProvider (selector, createProvider()) |> context.subscriptions.Add
        commands.registerCommand("fsharp.simplifyNameFix",Func<obj,obj,obj,obj>(fun a b c -> applyQuickFix(a |> unbox, b |> unbox, c |> unbox) |> unbox )) |> context.subscriptions.Add
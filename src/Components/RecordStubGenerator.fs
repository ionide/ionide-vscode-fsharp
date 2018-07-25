namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers

open DTO
open Ionide.VSCode.Helpers

module RecordStubGenerator =
    let mutable private currentDiagnostic = languages.createDiagnosticCollection ()
    // We can only have one suggestion at a time so using an Option field is enough
    let mutable private suggestion : RecordStubCaseGenerator option = None

    let private createProvider () =
        { new CodeActionProvider
          with
            member __.provideCodeActions(doc, _, context, _) =
                promise {
                    let res =
                        match suggestion with
                        | Some suggestion ->
                            let cmd = createEmpty<Command>
                            cmd.title <- "Generate record stubs"
                            cmd.command <- "fsharp.generateRecordStub"
                            cmd.arguments <- Some ([| doc |> unbox; suggestion |> unbox|] |> ResizeArray)

                            [| cmd |]
                        | None -> [||]

                    return res |> ResizeArray
                } |> U2.Case2
            }

    let findSuggestions (ev : TextEditorSelectionChangeEvent) =
        /// Offset line and col by `1` in order to match FSAC Position
        let line = int ev.textEditor.selection.start.line + 1
        let col = int ev.textEditor.selection.start.character + 1

        promise {
            let! res = LanguageService.recordStubGenerator ev.textEditor.document.fileName line col
            // If a suggestion has been found
            if JS.isDefined res then
                // Store the suggestion information
                suggestion <- Some res.Data
                let uri = Uri.file ev.textEditor.document.fileName
                let range =
                    Position(ev.textEditor.selection.start.line, ev.textEditor.selection.start.character)
                    |> ev.textEditor.document.getWordRangeAtPosition
                let diagnostics = [| Diagnostic(range, "Generate record stubs", DiagnosticSeverity.Information) |] |> ResizeArray
                // Add
                currentDiagnostic.set(uri, diagnostics)
            else
                // No suggestion found clean preivous result and diagnostic
                suggestion <- None
                currentDiagnostic.clear()
        }
        |> ignore

    let insertText (doc : TextDocument) (text : string) (pos : Pos) =
        let edit = WorkspaceEdit()
        let uri = Uri.file doc.fileName
        let position = Position(unbox (pos.Line - 1), unbox pos.Column)
        let text = text.Replace("$1", "failwith \"Not Implemented\"")
        edit.insert(uri, position, text)
        workspace.applyEdit edit

    let testCommand (doc : TextDocument) (info : RecordStubCaseGenerator) =
        insertText doc info.Text info.Position
        |> ignore
        // Suggestion has been used clear it
        suggestion <- None
        currentDiagnostic.clear()

    let activate selector (context: ExtensionContext) =
        languages.registerCodeActionsProvider (selector, createProvider()) |> context.subscriptions.Add

        vscode.window.onDidChangeTextEditorSelection $ (findSuggestions, (), context.subscriptions) |> ignore

        commands.registerCommand("fsharp.generateRecordStub", testCommand |> unbox<Func<obj,obj,obj>>)
        |> context.subscriptions.Add

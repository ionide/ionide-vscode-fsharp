namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Ionide.VSCode.Helpers
open DTO

module InterfaceStubGenerator =

    let mutable private currentDiagnostic = languages.createDiagnosticCollection ()
    // We can only have one suggestion at a time so using an Option field is enough
    let mutable private suggestion : InterfaceStubGenerator option = None

    let private createProvider () =
        { new CodeActionProvider
          with
            member __.provideCodeActions(doc, _, context, _) =
                promise {
                    let res =
                        match suggestion with
                        | Some suggestion ->
                            let cmd = createEmpty<Command>
                            cmd.title <- "Generate interface stubs"
                            cmd.command <- "fsharp.generateInterfaceStub"
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
        match ev.textEditor.document with
        | Document.FSharp ->
            promise {
                let! res = LanguageService.interfaceStubGenerator ev.textEditor.document.fileName line col
                // If a suggestion has been found
                if JS.isDefined res then
                    // Store the suggestion information
                    suggestion <- Some res.Data
                    let uri = Uri.file ev.textEditor.document.fileName
                    let range =
                        Position(ev.textEditor.selection.start.line, ev.textEditor.selection.start.character)
                        |> ev.textEditor.document.getWordRangeAtPosition
                    let diagnostics = [| Diagnostic(range, "Generate interface stubs", DiagnosticSeverity.Hint) |] |> ResizeArray
                    // Add
                    currentDiagnostic.set(uri, diagnostics)
                else
                    // No suggestion found clean preivous result and diagnostic
                    suggestion <- None
                    currentDiagnostic.clear()
            }
            |> ignore
        | _ -> ()

    let mutable private timer = None

    let selectionHandler (ev : TextEditorSelectionChangeEvent) =
        timer |> Option.iter(clearTimeout)
        timer <- Some (setTimeout (fun _ -> findSuggestions ev ) 500.)

    let insertText (doc : TextDocument) (text : string) (pos : Pos) =
        let edit = WorkspaceEdit()
        let uri = Uri.file doc.fileName

        let insertPosition = Position(unbox (pos.Line - 1), unbox pos.Column)

        let objectIdentifier = "FSharp.interfaceStubGenerationObjectIdentifier" |> Configuration.get "this"
        let defaultBody = "FSharp.interfaceStubGenerationMethodBody" |> Configuration.get "failwith \"Not Implemented\""

        let formattedText =
            text
                .Replace("$objectIdent", objectIdentifier)
                .Replace("$methodBody", defaultBody)

        edit.insert(uri, insertPosition, formattedText)
        workspace.applyEdit edit

    let replaceCommand (doc : TextDocument) (info : InterfaceStubGenerator) =
        insertText doc info.Text info.Position
        |> ignore
        // Suggestion has been used clear it
        suggestion <- None
        currentDiagnostic.clear()

    let activate selector (context : ExtensionContext) =
        let isEnabled = "FSharp.interfaceStubGeneration" |> Configuration.get true

        if isEnabled then
            languages.registerCodeActionsProvider (selector, createProvider()) |> context.subscriptions.Add

            vscode.window.onDidChangeTextEditorSelection $ (selectionHandler, (), context.subscriptions) |> ignore

            commands.registerCommand("fsharp.generateInterfaceStub", replaceCommand |> unbox<Func<obj,obj,obj>>)
            |> context.subscriptions.Add

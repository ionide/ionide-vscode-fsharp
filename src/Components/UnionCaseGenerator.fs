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

module UnionCaseGenerator =
    let private createProvider () =

        { new CodeActionProvider
          with
            member this.provideCodeActions(doc, range, context, ct) =
                promise {
                    let diagnostic = context.diagnostics |> Seq.tryFind (fun d -> d.message.Contains "Incomplete pattern matches on this expression. For example")
                    let! res =
                        match diagnostic with
                        | None -> promise { return [||]}
                        | Some d ->
                            promise {
                                let line = int range.start.line + 2
                                let col = doc.getText().Split('\n').[line - 1].IndexOf('|') + 3


                                let! res = LanguageService.unionCaseGenerator doc.fileName line col

                                let cmd = createEmpty<Command>
                                cmd.title <- "Generate union pattern match case"
                                cmd.command <- "fsharp.insertUnionCases"
                                cmd.arguments <- Some ([| doc |> unbox; res.Data.Text |> unbox; res.Data.Position |> unbox; |] |> ResizeArray)

                                return [| cmd |]
                            }
                    return res |> ResizeArray
                } |> U2.Case2
            }

    let insertText (doc : TextDocument, text : string, pos : Pos) =
        let edit = WorkspaceEdit()
        let uri = Uri.file doc.fileName
        let position = Position(unbox (pos.Line - 1), unbox pos.Col)
        let text = text.Replace("$1", "failwith \"Not Implemented\"")
        edit.insert(uri, position, text)
        workspace.applyEdit edit

    let activate selector (context: ExtensionContext) =
        languages.registerCodeActionsProvider (selector, createProvider()) |> context.subscriptions.Add
        commands.registerCommand("fsharp.insertUnionCases",Func<obj,obj,obj,obj>(fun a b c -> insertText(a |> unbox, b |> unbox, c |> unbox) |> unbox )) |> context.subscriptions.Add

        ()

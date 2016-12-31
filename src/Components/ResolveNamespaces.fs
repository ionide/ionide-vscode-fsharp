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

module ResolveNamespaces =
    let private createProvider () =

        { new CodeActionProvider
          with
            member this.provideCodeActions(doc, range, context, ct) =
                promise {
                    let diagnostic = context.diagnostics |> Seq.tryFind (fun d -> d.message.Contains "is not defined")
                    let! res =
                        match diagnostic with
                        | None -> promise { return [||]}
                        | Some d ->
                            promise {
                                let! res = LanguageService.resolveNamespaces doc.fileName ( int range.start.line + 1) (int range.start.character + 2)
                                let word = res.Data.Word
                                let quals =
                                    res.Data.Qualifies
                                    |> Array.map (fun suggestion ->
                                        let s = suggestion.Qualifier
                                        let cmd = createEmpty<Command>
                                        cmd.title <- sprintf "Use %s" s
                                        cmd.command <- "fsharp.useNamespace"
                                        cmd.arguments <- Some ([| doc |> unbox; d.range |> unbox; s |> unbox; |] |> ResizeArray)
                                        cmd)

                                let opens =
                                    res.Data.Opens
                                    |> Array.map (fun suggestion ->
                                        let s = suggestion.Namespace
                                        let cmd = createEmpty<Command>
                                        cmd.title <- sprintf "Open %s" s
                                        cmd.command <- "fsharp.openNamespace"

                                        cmd.arguments <- Some ([| doc |> unbox; suggestion |> unbox; s |> unbox; |] |> ResizeArray)
                                        cmd)

                                return [| yield! quals; yield! opens |]
                            }
                    return res |> ResizeArray
                } |> Case2
            }

    let private getLineStr (doc : TextDocument) line = doc.getText(Range(line, 0., line, 1000.)).Trim()


    /// Corrects insertion line number based on kind of scope and text surrounding the insertion point.
    let private adjustInsertionPoint (doc : TextDocument) (ctx : OpenNamespace)  =
        let line =
            match ctx.Type with
            | "TopModule" ->
                if ctx.Line > 1 then
                    // it's an implicit module without any open declarations
                    let line = getLineStr doc (unbox (ctx.Line - 2))
                    let isImpliciteTopLevelModule = not (line.StartsWith "module" && not (line.EndsWith "="))
                    if isImpliciteTopLevelModule then 1 else ctx.Line
                else 1
            | "Namespace" ->
                // for namespaces the start line is start line of the first nested entity
                if ctx.Line > 1 then
                    [0..ctx.Line - 1]
                    |> List.mapi (fun i line -> i, getLineStr doc (unbox line))
                    |> List.tryPick (fun (i, lineStr) ->
                        if lineStr.StartsWith "namespace" then Some i
                        else None)
                    |> function
                        // move to the next line below "namespace" and convert it to F# 1-based line number
                        | Some line -> line + 2
                        | None -> ctx.Line
                else 1
            | _ -> ctx.Line

        { ctx with Line = line }

    let insertLine (doc : TextDocument) line lineStr =
        let edit = WorkspaceEdit()
        let uri = Uri.file doc.fileName
        let position = Position(unbox line, 0.)
        edit.insert(uri, position, lineStr)
        workspace.applyEdit edit


    let private applyQualify(doc : TextDocument, range : vscode.Range, suggestion : string) =
        let edit = WorkspaceEdit()
        let uri = Uri.file doc.fileName
        edit.replace(uri, range, suggestion)
        workspace.applyEdit edit

    let private applyOpen(doc : TextDocument, ctx : OpenNamespace, suggestion : string) =
        let ctx = adjustInsertionPoint doc ctx
        let docLine = ctx.Line - 1
        let lineStr = (String.replicate ctx.Column " ") + "open " + suggestion + "\n"
        promise {
            let! _ = insertLine doc docLine lineStr

            let! _ =
                if getLineStr doc (unbox docLine + 1.) <> "" then
                    insertLine doc (unbox docLine + 1.) ""
                else
                    Promise.lift false

            let! _ =
                if (ctx.Column = 0 || ctx.Type = "Namespace") && docLine > 0 && not ((getLineStr doc ( unbox docLine - 1.)).StartsWith "open" ) then
                    insertLine doc (unbox docLine) ""
                else
                    Promise.lift false

            return ()
        }





    let activate selector (disposables: Disposable[]) =
        languages.registerCodeActionsProvider (selector, createProvider()) |> ignore
        commands.registerCommand("fsharp.openNamespace",Func<obj,obj,obj,obj>(fun a b c -> applyOpen(a |> unbox, b |> unbox, c |> unbox) |> unbox )) |> ignore
        commands.registerCommand("fsharp.useNamespace",Func<obj,obj,obj,obj>(fun a b c -> applyQualify(a |> unbox, b |> unbox, c |> unbox) |> unbox )) |> ignore


        ()

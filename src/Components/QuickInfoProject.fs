namespace Ionide.VSCode.FSharp

open Fable.Import.vscode
open Fable.Import.Node

module node = Fable.Import.Node.Exports

module QuickInfoProject =

    let mutable private item : StatusBarItem option = None
    let private hideItem () =
        item |> Option.iter (fun n -> n.hide ())

    let mutable projectPath = ""

    let handler (te : TextEditor) =
        hideItem ()
        if te <> undefined && te.document <> undefined then
            let fileName = te.document.fileName
            let proj = Project.tryFindLoadedProjectByFile fileName
            match proj with
            | None ->
                match te.document with
                | Document.FSharp when path.extname te.document.fileName <> ".fsx" ->
                    item.Value.text <- "$(circuit-board) Not in a project"
                    item.Value.tooltip <- "This F# file is not in any project known to Ionide"
                    item.Value.command <- undefined
                    item.Value.color <- "#FFCC00"
                    item.Value.show()
                | _ -> ()
            | Some p ->
                projectPath <- p.Project
                let pPath = node.path.basename p.Project
                let text = sprintf "$(circuit-board) %s" pPath
                item.Value.text <- text
                item.Value.tooltip <- p.Project
                item.Value.command <- "openProjectFileFromStatusbar"
                item.Value.color <- undefined
                item.Value.show()

    let handlerCommand() =
        commands.executeCommand("vscode.open", Uri.file projectPath)


    let activate (context : ExtensionContext) =
        commands.registerCommand("openProjectFileFromStatusbar", unbox<System.Func<obj,obj>> (fun _ -> handlerCommand))
        |> context.subscriptions.Add
        item <- Some (window.createStatusBarItem (StatusBarAlignment.Right, 10000. ))
        context.subscriptions.Add(item.Value)

        window.onDidChangeActiveTextEditor.Invoke(unbox handler) |> context.subscriptions.Add
        if window.visibleTextEditors.Count > 0 then
            handler window.activeTextEditor

        Project.projectLoaded.Invoke(fun _project ->
            handler window.activeTextEditor
            undefined)
        |> context.subscriptions.Add

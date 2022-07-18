namespace Ionide.VSCode.FSharp

open Fable.Core
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode

module node = Node.Api

module QuickInfoProject =

    let mutable private item: StatusBarItem option = None

    let private hideItem () =
        item |> Option.iter (fun n -> n.hide ())

    let mutable projectPath = ""

    let handler (te: TextEditor option) =
        hideItem ()

        match te with
        | Some te ->
            let fileName = te.document.fileName
            let proj = Project.tryFindLoadedProjectByFile fileName

            match proj with
            | None ->
                match te.document with
                | Document.FSharp when node.path.extname te.document.fileName <> ".fsx" && not (te.document.isUntitled) ->
                    let loadingInfo =
                        if Project.isLoadingWorkspaceComplete () then
                            ""
                        else
                            " (Still loading...)"

                    let fileNameOnly = node.path.basename fileName

                    item.Value.text <- "$(circuit-board) Not in a F# project" + loadingInfo

                    item.Value.tooltip <- Some(U2.Case1 $"%s{fileNameOnly} is not in any project known to Ionide")
                    item.Value.command <- Some(U2.Case1 "fsharp.AddFileToProject")

                    item.Value.color <- vscode.ThemeColor.Create "fsharp.statusBarWarnings" |> U2.Case2 |> Some

                    item.Value.show ()
                | _ -> ()
            | Some p ->
                projectPath <- p.Project
                let pPath = node.path.basename p.Project
                let text = sprintf "$(circuit-board) %s" pPath
                item.Value.text <- text
                item.Value.tooltip <- Some(U2.Case1 p.Project)
                item.Value.command <- Some(U2.Case1 "openProjectFileFromStatusbar")
                item.Value.color <- undefined
                item.Value.show ()
        | None -> ()

    let handlerCommand () =
        commands.executeCommand ("vscode.open", Some(box (vscode.Uri.file projectPath)))


    let activate (context: ExtensionContext) =
        commands.registerCommand ("openProjectFileFromStatusbar", handlerCommand |> objfy2)
        |> context.Subscribe

        let statusBarItem = window.createStatusBarItem (StatusBarAlignment.Right, 10000.)
        item <- Some statusBarItem
        context.subscriptions.Add(unbox (box statusBarItem))

        window.onDidChangeActiveTextEditor.Invoke(unbox handler) |> context.Subscribe

        if window.visibleTextEditors.Count > 0 then
            handler window.activeTextEditor

        Project.projectLoaded.Invoke(fun _project ->
            handler window.activeTextEditor
            undefined)
        |> context.Subscribe

        Project.projectNotRestoredLoaded.Invoke(fun _project ->
            handler window.activeTextEditor
            undefined)
        |> context.Subscribe

        Project.workspaceChanged.Invoke(fun _workspacePeek ->
            handler window.activeTextEditor
            undefined)
        |> context.Subscribe

        Project.workspaceLoaded.Invoke(fun () ->
            handler window.activeTextEditor
            undefined)
        |> context.Subscribe

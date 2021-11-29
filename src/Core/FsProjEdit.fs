namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open global.Node

open DTO
open Ionide.VSCode.Helpers

module node = Node.Api

module FsProjEdit =

    let moveFileUpPath project file =
        LanguageService.fsprojMoveFileUp project file

    let moveFileDownPath project file =
        LanguageService.fsprojMoveFileDown project file

    let removeFilePath project file = Promise.empty
    //TODO
    //sprintf "remove file -n %s" (quotePath path) |> spawnForge |> ignore

    let addFileAbove project fromFile newFile =
        LanguageService.fsprojAddFileAbove project fromFile newFile

    let addFileBelow project fromFile newFile =
        LanguageService.fsprojAddFileBelow project fromFile newFile

    let addFile project file =
        LanguageService.fsprojAddFile project file


    let addCurrentFileToProject _ _ =
        promise {

            let projects = Project.getAll () |> ResizeArray

            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Choose a project"

                let! (projectPath: string option) = window.showQuickPick (projects |> U2.Case1, opts)

                return!
                    match projectPath with
                    | Some projectPath ->
                        match window.activeTextEditor with
                        | Some editor ->

                            let relativePathToFile =
                                let dir = path.dirname projectPath
                                path.relative (dir, editor.document.fileName)

                            addFile projectPath relativePathToFile
                        | None -> Promise.empty
                    | None -> Promise.empty
            else
                let! _ = window.showWarningMessage ("Can't find any project, run `dotnet new console -lang F#`", null)
                return ()
        }


    let addProjectReferencePath path =
        promise {
            let projects = Project.getAll () |> ResizeArray

            if projects.Count <> 0 then
                let opts = createEmpty<QuickPickOptions>
                opts.placeHolder <- Some "Reference"

                let! n = window.showQuickPick (projects |> U2.Case1, opts)

                return!
                    match n, path with
                    | Some n, Some path -> LanguageService.dotnetAddProject path n
                    | _ -> Promise.empty
        }

    let removeProjectReferencePath ref proj =
        LanguageService.dotnetRemoveProject proj ref

    let activate (context: ExtensionContext) =
        commands.registerTextEditorCommand ("fsharp.AddFileToProject", addCurrentFileToProject |> unbox)
        |> context.Subscribe

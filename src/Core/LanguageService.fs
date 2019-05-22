namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers

open DTO

module Notifications =
    type DocumentParsedEvent =
        { fileName : string
          text : string
          version : float
          /// BEWARE: Live object, might have changed since the parsing
          document : TextDocument
          result : ParseResult }

    let private onDocumentParsedEmitter = EventEmitter<DocumentParsedEvent>()
    let onDocumentParsed = onDocumentParsedEmitter.event

    let private tooltipRequestedEmitter = EventEmitter<Position>()
    let tooltipRequested = tooltipRequestedEmitter.event

    let mutable notifyWorkspaceHandler : Option<Choice<ProjectResult,ProjectLoadingResult,(string * ErrorData),string> -> unit> = None

module LanguageService =

    let compilerLocation () =
        //"" |> request<string, CompilerLocationResult> "compilerlocation" 0 (makeRequestId())
        undefined<CompilerLocationResult> |> Promise.lift

    let f1Help fn line col : JS.Promise<Result<string>> =
        // { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        // |> request "help" 0 (makeRequestId())
        undefined<Result<string>> |> Promise.lift

    let documentation fn line col =
        // { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        // |> request "documentation" 0 (makeRequestId())
        undefined |> Promise.lift

    let documentationForSymbol xmlSig assembly =
        // { DocumentationForSymbolReuqest.Assembly = assembly; XmlSig = xmlSig}
        // |> request "documentationForSymbol" 0 (makeRequestId())
        undefined |> Promise.lift

    let signature fn line col =
        // { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        // |> request<_, Result<string>> "signature" 0 (makeRequestId())
        undefined<Result<string>> |> Promise.lift

    let signatureData fn line col =
        // { PositionRequest.Line = line; FileName = handleUntitled fn; Column = col; Filter = "" }
        // |> request "signatureData" 0 (makeRequestId())
        undefined |> Promise.lift

    let declarations fn (text : string) version =
        // let lines = text.Replace("\uFEFF", "").Split('\n')
        // { DeclarationsRequest.FileName = handleUntitled fn; Lines = lines; Version = version }
        // |> request<_, Result<Symbols[]>> "declarations" 0 (makeRequestId())
        undefined |> Promise.lift

    let compile s =
        // { ProjectRequest.FileName = s }
        // |> request "compile" 0 (makeRequestId())
        undefined |> Promise.lift

    let fsdn (signature: string) =
        // let parse (ws : obj) =
        //     { FsdnResponse.Functions = ws?Functions |> unbox }

        // { FsdnRequest.Signature = signature }
        // |> requestCanFail "fsdn" 0 (makeRequestId())
        // |> Promise.map (fun res -> parse (res?Data |> unbox))

        undefined<DTO.FsdnResponse> |> Promise.lift

    let project s =
        let deserializeProjectResult (res : ProjectResult) =
            let parseInfo (f : obj) =
                match f?SdkType |> unbox with
                | "dotnet/sdk" ->
                    ProjectResponseInfo.DotnetSdk (f?Data |> unbox)
                | "verbose" ->
                    ProjectResponseInfo.Verbose
                | "project.json" ->
                    ProjectResponseInfo.ProjectJson
                | _ ->
                    f |> unbox

            { res with
                Data = { res.Data with
                            Info = parseInfo(res.Data.Info) } }
        // { ProjectRequest.FileName = s }
        // |> requestCanFail "project" 0 (makeRequestId())
        // |> Promise.map deserializeProjectResult
        // |> Promise.onFail(fun _ ->
        //     let disableShowNotification = "FSharp.disableFailedProjectNotifications" |> Configuration.get false
        //     if not disableShowNotification then
        //         let msg = "Project parsing failed: " + path.basename(s)
        //         vscode.window.showErrorMessage(msg, "Disable notification", "Show status")
        //         |> Promise.map(fun res ->
        //             if res = "Disable notification" then
        //                 Configuration.set "FSharp.disableFailedProjectNotifications" true
        //                 |> ignore
        //             if res = "Show status" then
        //                 ShowStatus.CreateOrShow(s, (path.basename(s)))
        //         )
        //         |> ignore
        // )
        undefined<ProjectResult> |> Promise.lift


    let workspacePeek dir deep excludedDirs =
        let rec mapItem (f : WorkspacePeekFoundSolutionItem) : WorkspacePeekFoundSolutionItem option =
            let mapItemKind (i : obj) : WorkspacePeekFoundSolutionItemKind option =
                let data = i?Data
                match i?Kind |> unbox with
                | "folder" ->
                    let folderData : WorkspacePeekFoundSolutionItemKindFolder = data |> unbox
                    let folder : WorkspacePeekFoundSolutionItemKindFolder =
                        { Files = folderData.Files
                          Items = folderData.Items |> Array.choose mapItem }
                    Some (WorkspacePeekFoundSolutionItemKind.Folder folder)
                | "msbuildFormat" ->
                    Some (WorkspacePeekFoundSolutionItemKind.MsbuildFormat (data |> unbox))
                | _ ->
                    None
            match mapItemKind f.Kind with
            | Some kind ->
                Some
                   { WorkspacePeekFoundSolutionItem.Guid = f.Guid
                     Name = f.Name
                     Kind = kind }
            | None -> None

        let mapFound (f : obj) : WorkspacePeekFound option =
            let data = f?Data
            match f?Type |> unbox with
            | "directory" ->
                Some (WorkspacePeekFound.Directory (data |> unbox))
            | "solution" ->
                let sln =
                    { WorkspacePeekFoundSolution.Path = data?Path |> unbox
                      Configurations = data?Configurations |> unbox
                      Items = data?Items |> unbox |> Array.choose mapItem }
                Some (WorkspacePeekFound.Solution sln)
            | _ ->
                None

        let parse (ws : obj) =
            { WorkspacePeek.Found = ws?Found |> unbox |> Array.choose mapFound }

        // { WorkspacePeekRequest.Directory = dir; Deep = deep; ExcludedDirs = excludedDirs |> Array.ofList }
        // |> request "workspacePeek" 0 (makeRequestId())
        // |> Promise.map (fun res -> parse (res?Data |> unbox))
        undefined<WorkspacePeek> |> Promise.lift

    let workspaceLoad disableInMemoryProject projects  =
        // { WorkspaceLoadRequest.Files = projects |> List.toArray; DisableInMemoryProjectReferences = disableInMemoryProject }
        // |> request "workspaceLoad" 0 (makeRequestId())
        undefined |> Promise.lift

    let private fsacConfig () =
        compilerLocation ()
        |> Promise.map (fun c -> c.Data)

    let fsi () =
        let fileExists (path: string): JS.Promise<bool> =
            Promise.create(fun success _failure ->
                fs.access(!^path, fs.constants.F_OK, fun err -> success(err.IsNone)))

        let getAnyCpuFsiPathFromCompilerLocation (location: CompilerLocation) = promise {
            match location.Fsi with
            | Some fsi ->
                // Only rewrite if FSAC returned 'fsi.exe' (For future-proofing)
                if path.basename fsi = "fsi.exe" then
                    // If there is an anyCpu variant in the same dir we do the rewrite
                    let anyCpuFile = path.join [| path.dirname fsi; "fsiAnyCpu.exe"|]
                    let! anyCpuExists = fileExists anyCpuFile
                    if anyCpuExists then
                        return Some anyCpuFile
                    else
                        return Some fsi
                else
                    return Some fsi
            | None ->
                return None
        }

        promise {
            match Environment.configFSIPath with
            | Some path -> return Some path
            | None ->
                let! fsacPaths = fsacConfig ()
                let! fsiPath = getAnyCpuFsiPathFromCompilerLocation fsacPaths
                return fsiPath
        }

    let fsc () =
        promise {
            match Environment.configFSCPath with
            | Some path -> return Some path
            | None ->
                let! fsacPaths = fsacConfig ()
                return fsacPaths.Fsc
        }

    let msbuild () =
        promise {
            match Environment.configMSBuildPath with
            | Some path -> return Some path
            | None ->
                let! fsacPaths = fsacConfig ()
                return fsacPaths.MSBuild
        }

    let start () =
        promise {
            return ()
        }

    let stop () =
        promise {
            return ()
        }
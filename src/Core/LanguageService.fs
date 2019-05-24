namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers

open DTO
open LanguageServer

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
    module internal Types =
        type PlainNotification= { content: string }

        /// Position in a text document expressed as zero-based line and zero-based character offset.
        /// A position is between two characters like an ‘insert’ cursor in a editor.
        type Position = {
            /// Line position in a document (zero-based).
            Line: int

            /// Character offset on a line in a document (zero-based). Assuming that the line is
            /// represented as a string, the `character` value represents the gap between the
            /// `character` and `character + 1`.
            ///
            /// If the character value is greater than the line length it defaults back to the
            /// line length.
            Character: int
        }

        type DocumentUri = string

        type TextDocumentIdentifier = {Uri: DocumentUri }

        type TextDocumentPositionParams = {
            TextDocument: TextDocumentIdentifier
            Position: Position
        }

        type FileParams = {
            Project: TextDocumentIdentifier
        }

        type WorkspaceLoadParms = {
            TextDocuments: TextDocumentIdentifier[]
        }

    let mutable client : LanguageClient option = None

    let private handleUntitled (fn : string) = if fn.EndsWith ".fs" || fn.EndsWith ".fsi" || fn.EndsWith ".fsx" then fn else (fn + ".fsx")


    let compilerLocation () =
        match client with
        | None -> Promise.empty
        | Some cl ->
            cl.sendRequest("fsharp/compilerLocation", null)
            |> Promise.map (fun (res: Types.PlainNotification) ->
                let r = res.content |> ofJson<CompilerLocationResult>
                r
            )


    let f1Help fn line col : JS.Promise<Result<string>> =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req : Types.TextDocumentPositionParams= {
                TextDocument = {Uri = handleUntitled fn}
                Position = {Line = line; Character = col}
            }

            cl.sendRequest("fsharp/f1Help", req )
            |> Promise.map (fun (res: Types.PlainNotification) ->
                res.content |> ofJson<Result<string>>
            )

    let documentation fn line col =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req : Types.TextDocumentPositionParams= {
                TextDocument = {Uri = handleUntitled fn}
                Position = {Line = line; Character = col}
            }

            cl.sendRequest("fsharp/documentation", req )
            |> Promise.map (fun (res: Types.PlainNotification) ->
                res.content |> ofJson<Result<DocumentationDescription[][]>>
            )

    let documentationForSymbol xmlSig assembly =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req = { DocumentationForSymbolReuqest.Assembly = assembly; XmlSig = xmlSig}

            cl.sendRequest("fsharp/documentationSymbol", req )
            |> Promise.map (fun (res: Types.PlainNotification) ->
                res.content |> ofJson<Result<DocumentationDescription[][]>>
            )

    let signature fn line col =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req : Types.TextDocumentPositionParams= {
                TextDocument = {Uri = handleUntitled fn}
                Position = {Line = line; Character = col}
            }

            cl.sendRequest("fsharp/signature", req )
            |> Promise.map (fun (res: Types.PlainNotification) ->
                res.content |> ofJson<Result<string>>
            )

    let signatureData fn line col =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req : Types.TextDocumentPositionParams= {
                TextDocument = {Uri = handleUntitled fn}
                Position = {Line = line; Character = col}
            }

            cl.sendRequest("fsharp/signatureData", req)
            |> Promise.map (fun (res: Types.PlainNotification) ->
                res.content |> ofJson<SignatureDataResult>
            )

    let lineLenses fn  =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req : Types.FileParams= {
                Project = {Uri = handleUntitled fn}
            }
            cl.sendRequest("fsharp/lineLens", req)
            |> Promise.map (fun (res: Types.PlainNotification) ->
                res.content |> ofJson<Result<Symbols[]>>
            )

    let compile s =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req : Types.FileParams= {
                Project = {Uri = handleUntitled s}
            }
            cl.sendRequest("fsharp/compile", req)
            |> Promise.map (fun (res: Types.PlainNotification) ->
                res.content |> ofJson<CompileResult>
            )

    let fsdn (signature: string) =
        let parse (ws : obj) =
            { FsdnResponse.Functions = ws?Functions |> unbox }

        match client with
        | None -> Promise.empty
        | Some cl ->
            let req : Types.FileParams= {
                Project = {Uri = signature}
            }
            cl.sendRequest("fsharp/fsdn", req)
            |> Promise.map (fun (res: Types.PlainNotification) ->
                let res = res.content |> ofJson<obj>
                parse (res?Data |> unbox)
            )


    let private deserializeProjectResult (res : ProjectResult) =
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

    let parseError (err : obj) =
        let data =
            match err?Code |> unbox with
            | ErrorCodes.GenericError ->
                ErrorData.GenericError
            | ErrorCodes.ProjectNotRestored ->
                ErrorData.ProjectNotRestored (err?AdditionalData |> unbox)
            | ErrorCodes.ProjectParsingFailed ->
                ErrorData.ProjectParsingFailed (err?AdditionalData |> unbox)
            | unknown ->
                //todo log not recognized for Debug
                ErrorData.GenericError
        (err?Message |> unbox<string>), data

    let project s =

        match client with
        | None -> Promise.empty
        | Some cl ->
            let req : Types.FileParams= {
                Project = {Uri = handleUntitled s}
            }
            cl.sendRequest("fsharp/project", req)
            |> Promise.map (fun (res: Types.PlainNotification) ->
                let res = res.content |> ofJson<obj>
                deserializeProjectResult (res?Data |> unbox)
            )
            |> Promise.onFail(fun _ ->
                let disableShowNotification = "FSharp.disableFailedProjectNotifications" |> Configuration.get false
                if not disableShowNotification then
                    let msg = "Project parsing failed: " + path.basename(s)
                    vscode.window.showErrorMessage(msg, "Disable notification", "Show status")
                    |> Promise.map(fun res ->
                        if res = "Disable notification" then
                            Configuration.set "FSharp.disableFailedProjectNotifications" true
                            |> ignore
                        if res = "Show status" then
                            ShowStatus.CreateOrShow(s, (path.basename(s)))
                    )
                    |> ignore
            )


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

        match client with
        | None -> Promise.empty
        | Some cl ->
            let req = { WorkspacePeekRequest.Directory = dir; Deep = deep; ExcludedDirs = excludedDirs |> Array.ofList }
            cl.sendRequest("fsharp/workspacePeek", req)
            |> Promise.map (fun (res: Types.PlainNotification) ->
                let res = res.content |> ofJson<obj>
                parse (res?Data |> unbox)
            )

    let workspaceLoad (projects: string list)  =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req : Types.WorkspaceLoadParms= {
                TextDocuments = projects |> List.map (fun s -> {Types.Uri = s}) |> List.toArray
            }
            cl.sendRequest("fsharp/workspaceLoad", req)
            |> Promise.map (fun (res: Types.PlainNotification) ->
                ()
            )

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

    let start (c : ExtensionContext) =
        promise {


            let ionidePluginPath = VSCodeExtension.ionidePluginPath () + "/bin_netcore/fsautocomplete.dll"
            let args =
                [
                    ionidePluginPath
                    "--mode"
                    "lsp"
                ] |> ResizeArray
            let runOpts = createObj [
                "command" ==> "dotnet"
                "args" ==> args
                "transport" ==> 0
            ]
            let debugOpts = createObj [
                "command" ==> "dotnet"
                "args" ==> args
                "transport" ==> 0
            ]

            let options =
                createObj [
                    "run" ==> runOpts
                    "debug" ==> debugOpts
                ] |> unbox<ServerOptions>

            let fileDeletedWatcher = workspace.createFileSystemWatcher("**/*.{fs,fsx}", true, true, false)

            let clientOpts =
                let opts = createEmpty<Client.LanguageClientOptions>
                let selector =
                    createObj [
                        "scheme" ==> "file"
                        "language" ==> "fsharp"
                    ] |> unbox<Client.DocumentSelector>

                let initOpts =
                    let backgroundSymbolCache = "FSharp.enableBackgroundSymbolCache" |> Configuration.get false
                    createObj [
                        "AutomaticWorkspaceInit" ==> false
                        "enableBackgroundSymbolCache" ==> backgroundSymbolCache
                    ]

                let synch = createEmpty<Client.SynchronizeOptions>
                synch.configurationSection <- Some !^"FSharp"
                synch.fileEvents <- Some( !^ ResizeArray([fileDeletedWatcher]))

                opts.documentSelector <- Some !^selector
                opts.synchronize <- Some synch
                opts.revealOutputChannelOn <- Some Client.RevealOutputChannelOn.Never


                opts.initializationOptions <- Some !^(Some initOpts)

                opts

            let cl = LanguageClient("FSharp", "F#", options, clientOpts, false)
            client <- Some cl
            cl.start () |> c.subscriptions.Add
            return!
                cl.onReady ()
                |> Promise.onSuccess (fun _ ->
                    cl.onNotification("fsharp/notifyWorkspace", (fun (a: Types.PlainNotification) ->
                        match Notifications.notifyWorkspaceHandler with
                        | None -> ()
                        | Some cb ->
                            let onMessage res =
                                match res?Kind |> unbox with
                                | "project" ->
                                    res |> unbox<ProjectResult> |> deserializeProjectResult |> Choice1Of4 |> cb
                                | "projectLoading" ->
                                    res |> unbox<ProjectLoadingResult> |> Choice2Of4 |> cb
                                | "error" ->
                                    res?Data |> parseError |> Choice3Of4 |> cb
                                | "workspaceLoad" ->
                                    res?Data?Status |> unbox<string> |> Choice4Of4 |> cb
                                | _ ->
                                    ()
                            let res = a.content |> ofJson<obj>
                            onMessage res
                    ))
                )
        }

    let stop () =
        promise {
            return ()
        }
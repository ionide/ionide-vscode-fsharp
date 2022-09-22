namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open Ionide.VSCode.Helpers
open Semver

open DTO
open LanguageServer

module node = Node.Api

module Notifications =
    type DocumentParsedEvent =
        {
            uri: string
            version: float
            /// BEWARE: Live object, might have changed since the parsing
            document: TextDocument
        }

    let onDocumentParsedEmitter = vscode.EventEmitter.Create<DocumentParsedEvent>()
    let onDocumentParsed = onDocumentParsedEmitter.event

    let private tooltipRequestedEmitter = vscode.EventEmitter.Create<Position>()
    let tooltipRequested = tooltipRequestedEmitter.event

    let mutable notifyWorkspaceHandler: Option<Choice<ProjectResult, ProjectLoadingResult, (string * ErrorData), string>
                                                   -> unit> =
        None

    let testDetectedEmitter = vscode.EventEmitter.Create<TestForFile>()
    let testDetected = testDetectedEmitter.event

module LanguageService =
    module Types =
        open Fable.Import.VSCode.Vscode
        type PlainNotification = { content: string }


        /// Position in a text document expressed as zero-based line and zero-based character offset.
        /// A position is between two characters like an ‘insert’ cursor in a editor.
        type Position =
            {
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

        type TextDocumentIdentifier = { Uri: DocumentUri }
        type VersionedTextDocumentIdentifier = { Uri: DocumentUri; Version: float }

        type TextDocumentPositionParams =
            { TextDocument: TextDocumentIdentifier
              Position: Position }

        type VersionedTextDocumentPositionParams =
            { TextDocument: VersionedTextDocumentIdentifier
              Position: Position }

        type FileParams = { Project: TextDocumentIdentifier }

        type WorkspaceLoadParms =
            { TextDocuments: TextDocumentIdentifier[] }

        type HighlightingRequest =
            { TextDocument: TextDocumentIdentifier }

        type FSharpLiterateRequest =
            { TextDocument: TextDocumentIdentifier }

        type FSharpPipelineHintsRequest =
            { TextDocument: TextDocumentIdentifier }

        type LspRange =
            { start: Fable.Import.VSCode.Vscode.Position
              ``end``: Fable.Import.VSCode.Vscode.Position }

    type Uri with

        member uri.ToDocumentUri = uri.ToString()

    let mutable client: LanguageClient option = None

    //TODO: remove (-> use URI instead)
    let private handleUntitled (fn: string) =
        if fn.EndsWith ".fs" || fn.EndsWith ".fsi" || fn.EndsWith ".fsx" then
            fn
        else
            (fn + ".fsx")


    let compilerLocation () =
        match client with
        | None -> Promise.empty
        | Some cl ->
            cl.sendRequest ("fsharp/compilerLocation", null)
            |> Promise.map (fun (res: Types.PlainNotification) ->
                let r = res.content |> ofJson<CompilerLocationResult>
                r)

    let tryFindDotnet () =
        let reportError (msg: string) =
            promise {
                let! selection = window.showErrorMessage ("Could not find 'dotnet'", "More details")

                match selection with
                | Some "More details" ->
                    // Prepare a temporary file to store the error message
                    let fileName =
                        "ionide_dotnet_not_found_details_"
                        + node.crypto.randomBytes(4).readUInt32LE(0).ToString()
                        + ".md"

                    let tempPath = node.path.join (node.os.tmpdir (), fileName)
                    // Write the error message to the file, this will allow us to close the text editor
                    // without a confirmation dialog as the file content will not be modified.
                    node.fs.writeFileSync (tempPath, msg)
                    // Open the file in VSCode
                    let newFile = vscode.Uri.parse (tempPath)
                    let! document = workspace.openTextDocument newFile
                    let! _ = window.showTextDocument (document, ?options = None)
                    // Show the error message in the markdown preview (better display)
                    do! commands.executeCommand ("markdown.showPreview", Some(box document))
                    // Close the text editor (this does not close the markdown preview)
                    do! commands.executeCommand ("workbench.action.closeActiveEditor", Some(box document))
                    // Delete the temporary file as it is not needed anymore.
                    node.fs.unlinkSync (U2.Case1 tempPath)
                | Some _
                | None -> ()
            }

        promise {
            // User has specified a custom dotnet location
            match Configuration.tryGet "FSharp.dotnetRoot" with
            | Some dotnetRoot ->
                // Choose the right program name depending on the OS
                let program = if Environment.isWin then "dotnet.exe" else "dotnet"

                // Compute the full path
                let dotnetFullPath = node.path.join (dotnetRoot, program)

                // Check if the program exists at the computed location
                if node.fs.existsSync (U2.Case1 dotnetFullPath) then
                    return Ok dotnetFullPath

                else
                    let msg =
                        // The special syntax: %s{"\n" + dotnetFullPath}
                        // Force a new line to be added, if I put %s{dotnetFullPath}
                        // at the beginning of the next line, there is a compiler error...
                        $"""
Could not find `dotnet` in the `dotnetRoot` directory: %s{"\n" + dotnetFullPath}

Consider:
- updating the `FSharp.dotnetRoot` settings key to a directory with a `dotnet` binary
- removing the `FSharp.dotnetRoot` settings key and :
    - including `dotnet` in your PATH
    - installing .NET Core"""

                    do! reportError msg

                    return Core.Error "Could not find 'dotnet'"

            // No custom location was specified, try to find it from the PATH
            | None ->
                match! Environment.tryGetTool "dotnet" with
                | Some dotnetFullPath -> return Ok dotnetFullPath

                | None ->
                    let msg =
                        """Could not find `dotnet` in the path.

Consider:
- setting the `FSharp.dotnetRoot` settings key to a directory with a `dotnet` binary
- including `dotnet` in your PATH
- installing .NET Core
"""

                    do! reportError msg

                    return Core.Error "Could not find 'dotnet'"
        }

    /// runs `dotnet --version` in the current rootPath to determine the resolved sdk version from the global.json file.
    let runtimeVersion () =
        promise {
            let! dotnet = tryFindDotnet ()

            match dotnet with
            | Error msg -> return Core.Error msg
            | Ok dotnet ->
                let! (error, stdout, stderr) = Process.exec dotnet (ResizeArray [ "--version" ])

                match error with
                | Some e -> return Core.Error $"error while invoking: {e.message}"
                | None ->
                    let stdoutTrimmed = stdout.TrimEnd()
                    let semver = semver.parse (Some(U2.Case1 stdoutTrimmed))

                    match semver with
                    | None -> return Core.Error $"unable to parse version string '{stdoutTrimmed}'"
                    | Some semver -> return Core.Ok semver
        }


    let f1Help (uri: Uri) line col : JS.Promise<Result<string>> =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.TextDocumentPositionParams =
                { TextDocument = { Uri = uri.ToDocumentUri }
                  Position = { Line = line; Character = col } }

            cl.sendRequest ("fsharp/f1Help", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> res.content |> ofJson<Result<string>>)

    let documentation (uri: Uri) line col =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.TextDocumentPositionParams =
                { TextDocument = { Uri = uri.ToDocumentUri }
                  Position = { Line = line; Character = col } }

            cl.sendRequest ("fsharp/documentation", req)
            |> Promise.map (fun (res: Types.PlainNotification) ->
                res.content |> ofJson<Result<DocumentationDescription[][]>>)

    let documentationForSymbol xmlSig assembly =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req =
                { DocumentationForSymbolRequest.Assembly = assembly
                  XmlSig = xmlSig }

            cl.sendRequest ("fsharp/documentationSymbol", req)
            |> Promise.map (fun (res: Types.PlainNotification) ->
                res.content |> ofJson<Result<DocumentationDescription[][]>>)

    let signature (uri: Uri) line col =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.TextDocumentPositionParams =
                { TextDocument = { Uri = uri.ToDocumentUri }
                  Position = { Line = line; Character = col } }

            cl.sendRequest ("fsharp/signature", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> res.content |> ofJson<Result<string>>)

    let signatureData (uri: Uri) line col =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.TextDocumentPositionParams =
                { TextDocument = { Uri = uri.ToDocumentUri }
                  Position = { Line = line; Character = col } }

            cl.sendRequest ("fsharp/signatureData", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> res.content |> ofJson<SignatureDataResult>)

    let generateDocumentation (fileUri: Uri, version) (line, col) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.VersionedTextDocumentPositionParams =
                { TextDocument =
                    { Uri = fileUri.ToDocumentUri
                      Version = version }
                  Position = { Line = line; Character = col } }

            cl.sendRequest ("fsharp/documentationGenerator", req)
            |> Promise.map (fun _ -> ())

    let lineLenses (uri: Uri) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.FileParams = { Project = { Uri = uri.ToDocumentUri } }

            cl.sendRequest ("fsharp/lineLens", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> res.content |> ofJson<Result<Symbols[]>>)

    let compile s =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.FileParams = { Project = { Uri = handleUntitled s } }

            cl.sendRequest ("fsharp/compile", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> res.content |> ofJson<CompileResult>)

    let fsdn (signature: string) =
        let parse (ws: obj) =
            { FsdnResponse.Functions = ws?Functions |> unbox }

        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.FileParams = { Project = { Uri = signature } }

            cl.sendRequest ("fsharp/fsdn", req)
            |> Promise.map (fun (res: Types.PlainNotification) ->
                let res = res.content |> ofJson<obj>
                parse (res?Data |> unbox))

    let dotnetNewList () =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: DotnetNew.DotnetNewListRequest = { Query = "" }

            cl.sendRequest ("fsharp/dotnetnewlist", req)
            |> Promise.map (fun (res: Types.PlainNotification) ->
                let x = res.content |> ofJson<DotnetNew.DotnetNewListResponse>

                x.Data)

    let dotnetNewRun (template: string) (name: string option) (output: string option) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: DotnetNew.DotnetNewRunRequest =
                { Template = template
                  Output = output
                  Name = name }

            cl.sendRequest ("fsharp/dotnetnewrun", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> ())

    let dotnetAddProject (target: string) (reference: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetProjectRequest =
                { Target = target
                  Reference = reference }

            cl.sendRequest ("fsharp/dotnetaddproject", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> ())

    let dotnetRemoveProject (target: string) (reference: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetProjectRequest =
                { Target = target
                  Reference = reference }

            cl.sendRequest ("fsharp/dotnetremoveproject", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> ())

    let dotnetAddSln (target: string) (reference: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetProjectRequest =
                { Target = target
                  Reference = reference }

            cl.sendRequest ("fsharp/dotnetaddsln", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> ())

    let fsprojMoveFileUp (fsproj: string) (file: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetFileRequest =
                { FsProj = fsproj
                  FileVirtualPath = file }

            printfn "TEST8 %A" req

            cl.sendRequest ("fsproj/moveFileUp", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> ()

            )

    let fsprojMoveFileDown (fsproj: string) (file: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetFileRequest =
                { FsProj = fsproj
                  FileVirtualPath = file }

            cl.sendRequest ("fsproj/moveFileDown", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> ())

    let fsprojAddFileAbove (fsproj: string) (file: string) (newFile: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetFile2Request =
                { FsProj = fsproj
                  FileVirtualPath = file
                  NewFile = newFile }

            cl.sendRequest ("fsproj/addFileAbove", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> ())

    let fsprojAddFileBelow (fsproj: string) (file: string) (newFile: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetFile2Request =
                { FsProj = fsproj
                  FileVirtualPath = file
                  NewFile = newFile }

            cl.sendRequest ("fsproj/addFileBelow", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> ())

    let fsprojAddFile (fsproj: string) (file: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetFileRequest =
                { FsProj = fsproj
                  FileVirtualPath = file }

            cl.sendRequest ("fsproj/addFile", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> ())

    let fsprojRemoveFile (fsproj: string) (file: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetFileRequest =
                { FsProj = fsproj
                  FileVirtualPath = file }

            cl.sendRequest ("fsproj/removeFile", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> ())

    let parseError (err: obj) =
        let data =
            match err?Code |> unbox with
            | ErrorCodes.GenericError -> ErrorData.GenericError
            | ErrorCodes.ProjectNotRestored -> ErrorData.ProjectNotRestored(err?AdditionalData |> unbox)
            | ErrorCodes.ProjectParsingFailed -> ErrorData.ProjectParsingFailed(err?AdditionalData |> unbox)
            | ErrorCodes.LanguageNotSupported -> ErrorData.LangugageNotSupported(err?AdditionalData |> unbox)
            | unknown -> ErrorData.GenericError

        (err?Message |> unbox<string>), data

    let project s =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.FileParams = { Project = { Uri = s } }

            cl.sendRequest ("fsharp/project", req)
            |> Promise.map (fun (res: Types.PlainNotification) ->
                let res = res.content |> ofJson<obj>

                match res?Kind |> unbox with
                | "error" -> res?Data |> parseError |> Core.Result.Error
                | _ -> res |> unbox<ProjectResult> |> Ok)

    let workspacePeek dir deep excludedDirs =
        let rec mapItem (f: WorkspacePeekFoundSolutionItem) : WorkspacePeekFoundSolutionItem option =
            let mapItemKind (i: obj) : WorkspacePeekFoundSolutionItemKind option =
                let data = i?Data

                match i?Kind |> unbox with
                | "folder" ->
                    let folderData: WorkspacePeekFoundSolutionItemKindFolder = data |> unbox

                    let folder: WorkspacePeekFoundSolutionItemKindFolder =
                        { Files = folderData.Files
                          Items = folderData.Items |> Array.choose mapItem }

                    Some(WorkspacePeekFoundSolutionItemKind.Folder folder)
                | "msbuildFormat" -> Some(WorkspacePeekFoundSolutionItemKind.MsbuildFormat(data |> unbox))
                | _ -> None

            match mapItemKind f.Kind with
            | Some kind ->
                Some
                    { WorkspacePeekFoundSolutionItem.Guid = f.Guid
                      Name = f.Name
                      Kind = kind }
            | None -> None

        let mapFound (f: obj) : WorkspacePeekFound option =
            let data = f?Data

            match f?Type |> unbox with
            | "directory" -> Some(WorkspacePeekFound.Directory(data |> unbox))
            | "solution" ->
                let sln =
                    { WorkspacePeekFoundSolution.Path = data?Path |> unbox
                      Configurations = data?Configurations |> unbox
                      Items = data?Items |> unbox |> Array.choose mapItem }

                Some(WorkspacePeekFound.Solution sln)
            | _ -> None

        let parse (ws: obj) =
            { WorkspacePeek.Found = ws?Found |> unbox |> Array.choose mapFound }

        match client with
        | None -> Promise.empty
        | Some cl ->
            let req =
                { WorkspacePeekRequest.Directory = dir
                  Deep = deep
                  ExcludedDirs = excludedDirs |> Array.ofList }

            cl.sendRequest ("fsharp/workspacePeek", req)
            |> Promise.map (fun (res: Types.PlainNotification) ->
                let res = res.content |> ofJson<obj>
                parse (res?Data |> unbox))

    let workspaceLoad (projects: string list) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.WorkspaceLoadParms =
                { TextDocuments = projects |> List.map (fun s -> { Types.Uri = s }) |> List.toArray }

            cl.sendRequest ("fsharp/workspaceLoad", req) |> Promise.map ignore

    let loadAnalyzers () =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.FileParams = { Project = { Uri = "" } }

            cl.sendRequest ("fsharp/loadAnalyzers", req) |> Promise.map ignore

    let getHighlighting (uri: Uri) : JS.Promise<HighlightingResponse> =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.HighlightingRequest = { TextDocument = { Uri = uri.ToDocumentUri } }

            cl.sendRequest ("fsharp/highlighting", req)
            |> Promise.map (fun (res: obj) -> res?data)

    let fsharpLiterate (uri: Uri) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.FSharpLiterateRequest =
                { TextDocument = { Uri = uri.ToDocumentUri } }

            cl.sendRequest ("fsharp/fsharpLiterate", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> res.content |> ofJson<FSharpLiterateResult>)

    let pipelineHints (uri: Uri) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.FSharpPipelineHintsRequest =
                { TextDocument = { Uri = uri.ToDocumentUri } }

            cl.sendRequest ("fsharp/pipelineHint", req)
            |> Promise.map (fun (res: Types.PlainNotification) -> res.content |> ofJson<PipelineHintsResult>)

    let private fsacConfig () =
        compilerLocation () |> Promise.map (fun c -> c.Data)

    let fsi () =
        let fileExists (path: string) : JS.Promise<bool> =
            Promise.create (fun success _failure ->
                node.fs.access (!^path, node.fs.constants.F_OK, (fun err -> success (err.IsNone))))

        let getAnyCpuFsiPathFromCompilerLocation (location: CompilerLocation) =
            promise {
                match location.Fsi with
                | Some fsi ->
                    // Only rewrite if FSAC returned 'fsi.exe' (For future-proofing)
                    if node.path.basename fsi = "fsi.exe" then
                        // If there is an anyCpu variant in the same dir we do the rewrite
                        let anyCpuFile = node.path.join [| node.path.dirname fsi; "fsiAnyCpu.exe" |]

                        let! anyCpuExists = fileExists anyCpuFile

                        if anyCpuExists then
                            return Some anyCpuFile
                        else
                            return Some fsi
                    else
                        return Some fsi
                | None -> return None
            }

        promise {
            match Environment.configFsiFilePath () with
            | Some path -> return Some path
            | None ->
                let! fsacPaths = fsacConfig ()
                let! fsiPath = getAnyCpuFsiPathFromCompilerLocation fsacPaths
                return fsiPath
        }

    let fsiSdk () =
        promise { return Environment.configFsiSdkFilePath () }

    let private createClient (opts: Executable) =
        let options = createObj [ "run" ==> opts; "debug" ==> opts ] |> unbox<ServerOptions>

        let fileDeletedWatcher =
            workspace.createFileSystemWatcher (U2.Case1 "**/*.{fs,fsx}", true, true, false)

        let clientOpts =
            let opts = createEmpty<Client.LanguageClientOptions>

            let selector: DocumentSelector =
                let filter: DocumentFilter =
                    jsOptions<TextDocumentFilter> (fun f -> f.language <- Some "fsharp") |> U2.Case1

                [| U2.Case2 filter |]

            let initOpts = createObj [ "AutomaticWorkspaceInit" ==> false ]

            let synch = createEmpty<Client.SynchronizeOptions>
            synch.configurationSection <- Some !^ "FSharp"
            synch.fileEvents <- Some(!^ ResizeArray([ fileDeletedWatcher ]))

            // this type needs to be updated on the bindings - DocumentSelector is a (string|DocumentFilter) [] now only.
            // that's why we need to coerce it here.
            opts.documentSelector <- Some selector
            opts.synchronize <- Some synch
            opts.revealOutputChannelOn <- Some Client.RevealOutputChannelOn.Never

            opts.initializationOptions <- Some !^(Some initOpts)
            opts?markdown <- createObj [ "isTrusted" ==> true ]

            opts

        let cl = LanguageClient("FSharp", "F#", options, clientOpts, false)
        client <- Some cl
        cl

    let getOptions (c: ExtensionContext) : JS.Promise<Executable> =
        promise {

            let backgroundSymbolCache =
                "FSharp.enableBackgroundServices" |> Configuration.get true

            let enableProjectGraph =
                "FSharp.enableMSBuildProjectGraph" |> Configuration.get false

            let fsacAttachDebugger = "FSharp.fsac.attachDebugger" |> Configuration.get false

            let fsacNetcorePath = "FSharp.fsac.netCoreDllPath" |> Configuration.get ""

            let fsacSilencedLogs = "FSharp.fsac.silencedLogs" |> Configuration.get [||]

            let verbose = "FSharp.verboseLogging" |> Configuration.get false

            let fsacPathForTfm (tfm: string) =
                if String.IsNullOrEmpty fsacNetcorePath then
                    node.path.join (VSCodeExtension.ionidePluginPath (), "bin", tfm, "fsautocomplete.dll")
                else
                    fsacNetcorePath

            let tfmForSdkVersion (v: SemVer) =
                match int v.major, int v.minor with
                | 3, 1 -> "netcoreapp3.1"
                | 3, 0 -> "netcoreapp3.0"
                | 2, 1 -> "netcoreapp2.1"
                | 2, 0 -> "netcoreapp2.0"
                | n, _ -> $"net{n}.0"

            let discoverDotnetArgs () =
                promise {
                    let! (rollForwardArgs, necessaryEnvVariables, fsacPath) =
                        promise {
                            let! sdkVersionAtRootPath = runtimeVersion ()

                            match sdkVersionAtRootPath with
                            | Error e ->
                                printfn $"FSAC (NETCORE): {e}"
                                return [], [], ""
                            | Ok v ->
                                let tfm = tfmForSdkVersion v
                                let fsacPath = fsacPathForTfm tfm
                                if v.major >= 6.0 then
                                    // when we run on a sdk higher than 5.x (aka what FSAC is currently built/targeted for),
                                    // we have to tell the runtime to allow it to actually run on that runtime (instead of presenting 6.x as 5.x)
                                    // in order for msbuild resolution to work
                                    let args = [ "--roll-forward"; "LatestMajor" ]

                                    let envs =
                                        if v.prerelease <> null || v.prerelease.Count > 0 then
                                            [ "DOTNET_ROLL_FORWARD_TO_PRERELEASE", box 1 ]
                                        else
                                            []

                                    return args, envs, fsacPath
                                else
                                    return [], [], fsacPath
                        }

                    let userDotnetArgs = "FSharp.fsac.dotnetArgs" |> Configuration.get [||]

                    let hasUserRollForward =
                        userDotnetArgs
                        |> Array.tryFindIndex (fun a -> a = "--roll-forward")
                        |> Option.map (fun _ -> true)
                        |> Option.defaultValue false

                    let hasUserFxVersion =
                        userDotnetArgs
                        |> Array.tryFindIndex (fun a -> a = "--fx-version")
                        |> Option.map (fun _ -> true)
                        |> Option.defaultValue false

                    let shouldApplyImplicitRollForward = not (hasUserFxVersion || hasUserRollForward)

                    let args =
                        [ if shouldApplyImplicitRollForward then
                              yield! rollForwardArgs
                          yield! userDotnetArgs ]

                    let envVariables =
                        [ if shouldApplyImplicitRollForward then
                              yield! necessaryEnvVariables ]

                    return args, envVariables, fsacPath
                }

            let spawnNetCore dotnet : JS.Promise<Executable> =
                promise {
                    let! (fsacDotnetArgs, fsacEnvVars, fsacPath) = discoverDotnetArgs ()

                    printfn $"FSAC (NETCORE): '%s{fsacPath}'"

                    let exeOpts = createEmpty<ExecutableOptions>

                    let exeEnv =
                        match fsacEnvVars with
                        | [] -> None
                        | fsacEnvVars ->
                            // only need to set the process env if FSAC needs rollfoward env vars.
                            let keys = Node.Util.Object.keys node.``process``.env

                            let baseEnv = keys |> Seq.toList |> List.map (fun k -> k, node.``process``.env?(k))

                            let combinedEnv = baseEnv @ fsacEnvVars |> ResizeArray
                            let envObj = createObj combinedEnv
                            Some envObj

                    exeOpts.env <- exeEnv

                    let args =
                        [ yield! fsacDotnetArgs
                          yield fsacPath
                          if fsacAttachDebugger then
                              yield "--attachdebugger"
                              yield "--wait-for-debugger"
                          if backgroundSymbolCache then
                              yield "--background-service-enabled"
                          if enableProjectGraph then
                              yield "--project-graph-enabled"
                          if verbose then
                              yield "--verbose"
                          if fsacSilencedLogs <> null && fsacSilencedLogs.Length > 0 then
                              yield "--filter"
                              yield! fsacSilencedLogs
                          match c.storageUri with
                          | Some uri ->
                              let storageDir = uri.fsPath
                              yield "--state-directory"
                              yield storageDir
                          | None -> () ]
                        |> ResizeArray

                    let executable = createEmpty<Executable>
                    executable.command <- dotnet
                    executable.args <- Some args
                    executable.options <- Some exeOpts
                    return executable
                }

            let! dotnet = tryFindDotnet ()

            match dotnet with
            // dotnet SDK handling
            | Ok dotnet -> return! spawnNetCore dotnet
            | Error msg -> return failwith msg
        }

    let registerCustomNotifications (cl: LanguageClient) =
        cl.onNotification (
            "fsharp/notifyWorkspace",
            (fun (a: Types.PlainNotification) ->
                match Notifications.notifyWorkspaceHandler with
                | None -> ()
                | Some cb ->
                    let onMessage res =
                        match res?Kind |> unbox with
                        | "project" -> res |> unbox<ProjectResult> |> Choice1Of4 |> cb
                        | "projectLoading" -> res |> unbox<ProjectLoadingResult> |> Choice2Of4 |> cb
                        | "error" -> res?Data |> parseError |> Choice3Of4 |> cb
                        | "workspaceLoad" -> res?Data?Status |> unbox<string> |> Choice4Of4 |> cb
                        | _ -> ()

                    let res = a.content |> ofJson<obj>
                    onMessage res)
        )

        cl.onNotification (
            "fsharp/fileParsed",
            (fun (a: Types.PlainNotification) ->
                let uri: Types.DocumentUri = a.content

                window.visibleTextEditors
                |> Seq.tryFind (fun n -> n.document.uri.ToDocumentUri.ToLowerInvariant() = uri.ToLowerInvariant())
                |> Option.iter (fun te ->
                    let ev =
                        { Notifications.uri = uri
                          Notifications.version = te.document.version
                          Notifications.document = te.document }

                    Notifications.onDocumentParsedEmitter.fire ev))
        )

        cl.onNotification ("fsharp/testDetected", (fun (a: TestForFile) -> Notifications.testDetectedEmitter.fire a))

    let start (c: ExtensionContext) =
        promise {
            let! startOpts = getOptions c
            let cl = createClient startOpts
            registerCustomNotifications cl
            let started = cl.start ()
            c.subscriptions.Add(started |> box |> unbox)
            return ()
        }

    let stop () =
        promise {
            match client with
            | Some cl -> return! cl.stop ()
            | None -> return ()
        }

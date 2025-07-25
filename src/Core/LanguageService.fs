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

    let mutable notifyWorkspaceHandler
        : Option<Choice<ProjectResult, ProjectLoadingResult, (string * ErrorData), string> -> unit> =
        None

    let testDetectedEmitter = vscode.EventEmitter.Create<TestForFile>()
    let testDetected = testDetectedEmitter.event

module LanguageService =
    open Fable.Import.LanguageServer.Client

    let private logger =
        ConsoleAndOutputChannelLogger(Some "LanguageService", Level.DEBUG, Some defaultOutputChannel, Some Level.DEBUG)

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

    let inline checkNotificationAndCast<'t> (response: Types.PlainNotification) : 't option =
        if isNullOrUndefined response then
            None
        else
            Some(ofJson<'t> response.content)

    let selector: DocumentSelector =
        let fileSchemeFilter: DocumentFilter =
            jsOptions<TextDocumentFilter> (fun f ->
                f.language <- Some "fsharp"
                f.scheme <- Some "file")
            |> U2.Case1

        let untitledSchemeFilter: DocumentFilter =
            jsOptions<TextDocumentFilter> (fun f ->
                f.language <- Some "fsharp"
                f.scheme <- Some "untitled")
            |> U2.Case1

        [| U2.Case2 fileSchemeFilter; U2.Case2 untitledSchemeFilter |]

    //TODO: remove (-> use URI instead)
    let private handleUntitled (fn: string) =
        if fn.EndsWith ".fs" || fn.EndsWith ".fsi" || fn.EndsWith ".fsx" then
            fn
        else
            (fn + ".fsx")

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
    let sdkVersion () =
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


    let f1Help (uri: Uri) line col =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.TextDocumentPositionParams =
                { TextDocument = { Uri = uri.ToDocumentUri }
                  Position = { Line = line; Character = col } }

            cl.sendRequest ("fsharp/f1Help", req)
            |> Promise.map checkNotificationAndCast<Result<string>>

    let documentation (uri: Uri) line col =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.TextDocumentPositionParams =
                { TextDocument = { Uri = uri.ToDocumentUri }
                  Position = { Line = line; Character = col } }

            cl.sendRequest ("fsharp/documentation", req)
            |> Promise.map checkNotificationAndCast<Result<DocumentationDescription>>

    let documentationForSymbol xmlSig assembly =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req =
                { DocumentationForSymbolRequest.Assembly = assembly
                  XmlSig = xmlSig }

            cl.sendRequest ("fsharp/documentationSymbol", req)
            |> Promise.map checkNotificationAndCast<Result<DocumentationDescription>>

    let signature (uri: Uri) line col =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.TextDocumentPositionParams =
                { TextDocument = { Uri = uri.ToDocumentUri }
                  Position = { Line = line; Character = col } }

            cl.sendRequest ("fsharp/signature", req)
            |> Promise.map checkNotificationAndCast<Result<string>>

    let signatureData (uri: Uri) line col =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: Types.TextDocumentPositionParams =
                { TextDocument = { Uri = uri.ToDocumentUri }
                  Position = { Line = line; Character = col } }

            cl.sendRequest ("fsharp/signatureData", req)
            |> Promise.map checkNotificationAndCast<SignatureDataResult>

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
            |> Promise.map checkNotificationAndCast<Result<Symbols[]>>

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
            |> Promise.map checkNotificationAndCast<DotnetNew.DotnetNewListResponse>

    let dotnetNewRun (template: string) (name: string option) (output: string option) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: DotnetNew.DotnetNewRunRequest =
                { Template = template
                  Output = output
                  Name = name }

            cl.sendRequest ("fsharp/dotnetnewrun", req) |> Promise.map ignore

    let dotnetAddProject (target: string) (reference: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetProjectRequest =
                { Target = target
                  Reference = reference }

            cl.sendRequest ("fsharp/dotnetaddproject", req) |> Promise.map ignore

    let dotnetRemoveProject (target: string) (reference: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetProjectRequest =
                { Target = target
                  Reference = reference }

            cl.sendRequest ("fsharp/dotnetremoveproject", req) |> Promise.map ignore

    let dotnetAddSln (target: string) (reference: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetProjectRequest =
                { Target = target
                  Reference = reference }

            cl.sendRequest ("fsharp/dotnetaddsln", req) |> Promise.map ignore

    let fsprojMoveFileUp (fsproj: string) (file: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetFileRequest =
                { FsProj = fsproj
                  FileVirtualPath = file }

            cl.sendRequest ("fsproj/moveFileUp", req) |> Promise.map ignore

    let fsprojMoveFileDown (fsproj: string) (file: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetFileRequest =
                { FsProj = fsproj
                  FileVirtualPath = file }

            cl.sendRequest ("fsproj/moveFileDown", req) |> Promise.map ignore

    let fsprojAddFileAbove (fsproj: string) (file: string) (newFile: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetFile2Request =
                { FsProj = fsproj
                  FileVirtualPath = file
                  NewFile = newFile }

            cl.sendRequest ("fsproj/addFileAbove", req) |> Promise.map ignore

    let fsprojAddFileBelow (fsproj: string) (file: string) (newFile: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetFile2Request =
                { FsProj = fsproj
                  FileVirtualPath = file
                  NewFile = newFile }

            cl.sendRequest ("fsproj/addFileBelow", req) |> Promise.map ignore

    let fsprojAddFile (fsproj: string) (file: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetFileRequest =
                { FsProj = fsproj
                  FileVirtualPath = file }

            cl.sendRequest ("fsproj/addFile", req) |> Promise.map ignore

    let fsprojAddExistingFile (fsproj: string) (file: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetFileRequest =
                { FsProj = fsproj
                  FileVirtualPath = file }

            cl.sendRequest ("fsproj/addExistingFile", req) |> Promise.map ignore

    let fsprojRenameFile (fsproj: string) (file: string) (newFile: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetRenameFileRequest =
                { FsProj = fsproj
                  OldFileVirtualPath = file
                  NewFileName = newFile }

            cl.sendRequest ("fsproj/renameFile", req) |> Promise.map ignore

    let fsprojRemoveFile (fsproj: string) (file: string) =
        match client with
        | None -> Promise.empty
        | Some cl ->
            let req: FsProj.DotnetFileRequest =
                { FsProj = fsproj
                  FileVirtualPath = file }

            cl.sendRequest ("fsproj/removeFile", req) |> Promise.map ignore

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
            |> Promise.map checkNotificationAndCast<PipelineHintsResult>

    let fsiSdk () =
        promise { return Environment.configFsiSdkFilePath () }

    let testDiscovery s =
        match client with
        | None -> Promise.empty
        | Some cl ->
            cl.sendRequest ("test/discoverTests", ())
            |> Promise.map (fun (res: Types.PlainNotification) -> res.content |> ofJson<DiscoverTestsResult>)

    let private createClient (opts: Executable) =

        let options: ServerOptions = U5.Case2 {| run = opts; debug = opts |}

        let fileDeletedWatcher =
            workspace.createFileSystemWatcher (U2.Case1 "**/*.{fs,fsx}", true, true, false)

        let clientOpts =
            let opts = createEmpty<Client.LanguageClientOptions>

            let mutable initFails = 0

            let initializationFailureHandler (error: U3<ResponseError, Exception, obj option>) =
                if initFails < 5 then
                    logger.Error($"Initialization failed: %%", error)
                    initFails <- initFails + 1
                    true
                else
                    false


            let restarts = new ResizeArray<_>()

            let errorHandling =
                {

                  new ErrorHandler with
                      member this.closed() : CloseAction =
                          restarts.Add(1)

                          if restarts |> Seq.length < 5 then
                              CloseAction.Restart
                          else

                              logger.Error("Server closed")
                              CloseAction.DoNotRestart

                      member this.error(error: Exception, message: Message, count: float) : ErrorAction =
                          logger.Error($"Error from server: {error} {message} {count}")

                          if count < 3.0 then
                              ErrorAction.Continue
                          else
                              ErrorAction.Shutdown

                }

            let initOpts = createObj [ "AutomaticWorkspaceInit" ==> false ]

            let synch = createEmpty<Client.SynchronizeOptions>
            synch.configurationSection <- Some !^ "FSharp"
            synch.fileEvents <- Some(!^ ResizeArray([ fileDeletedWatcher ]))

            // this type needs to be updated on the bindings - DocumentSelector is a (string|DocumentFilter) [] now only.
            // that's why we need to coerce it here.
            opts.documentSelector <- Some selector
            opts.synchronize <- Some synch
            opts.errorHandler <- Some errorHandling
            opts.revealOutputChannelOn <- Some Client.RevealOutputChannelOn.Never
            // Worth keeping around for debug purposes
            // opts.traceOutputChannel <- Some defaultOutputChannel
            // opts.outputChannel <- Some defaultOutputChannel
            opts.initializationFailedHandler <- Some(!!initializationFailureHandler)

            opts.initializationOptions <- Some !^(Some initOpts)
            opts?markdown <- createObj [ "isTrusted" ==> true; "supportHtml" ==> true ]

            opts

        let cl = LanguageClient("FSharp", "F#", options, clientOpts, false)
        client <- Some cl
        cl

    let getOptions (c: ExtensionContext) : JS.Promise<Executable> =
        promise {
            let openTelemetryEnabled = "FSharp.openTelemetry.enabled" |> Configuration.get false

            let enableProjectGraph =
                "FSharp.enableMSBuildProjectGraph" |> Configuration.get false

            let useTransparentCompiler =
                "FSharp.fcs.transparentCompiler.enabled" |> Configuration.get false

            let tryBool x =
                // Boolean.TryParse generates: TypeError: e.match is not a function if we don't call toString first
                match Boolean.TryParse(x.ToString()) with
                | (true, v) -> Some v
                | _ -> None

            let tryInt x =
                match Int32.TryParse(x.ToString()) with
                | (true, v) -> Some v
                | _ -> None

            let oldgcConserveMemory =
                "FSharp.fsac.conserveMemory"
                |> Configuration.tryGet
                |> Option.map string
                |> Option.bind tryBool

            let gcConserveMemory =
                "FSharp.fsac.gc.conserveMemory" |> Configuration.tryGet |> Option.bind tryInt

            let gcConserveMemory =
                // prefer new setting, fallback to old, default is 0
                match gcConserveMemory, oldgcConserveMemory with
                | Some x, _ -> x
                | None, Some true -> 9
                | None, _ -> 0


            let gcServer = "FSharp.fsac.gc.server" |> Configuration.get true

            let gcServerUseDatas: bool option =
                "FSharp.fsac.gc.useDatas" |> Configuration.tryGet |> Option.bind tryBool

            let gcNoAffinitize =
                "Fsharp.fsac.gc.noAffinitize" |> Configuration.tryGet |> Option.bind tryBool

            let gcHeapCount =
                "FSharp.fsac.gc.heapCount" |> Configuration.tryGet |> Option.bind tryInt

            let parallelReferenceResolution =
                "FSharp.fsac.parallelReferenceResolution" |> Configuration.get false

            let fsacAttachDebugger = "FSharp.fsac.attachDebugger" |> Configuration.get false

            let fsacNetcorePath = "FSharp.fsac.netCoreDllPath" |> Configuration.get ""

            let fsacSilencedLogs = "FSharp.fsac.silencedLogs" |> Configuration.get [||]

            let verbose = "FSharp.verboseLogging" |> Configuration.get false

            /// given a set of tfms and a target tfm, find the first of the set that satisfies the target.
            /// if no target is found, use the 'latest' tfm
            /// e.g. [net6.0, net7.0] + net8.0 -> net7.0
            /// e.g. [net6.0, net7.0] + net7.0 -> net7.0
            let findBestTFM (availableTFMs: string seq) (tfm: string) =
                let tfmToSemVer (t: string) =
                    t.Replace("netcoreapp", "").Replace("net", "").Split([| '.' |], 2)
                    |> fun ver -> semver.parse (!! $"{ver[0]}.{ver[1]}.0")

                let tfmMap =
                    availableTFMs
                    |> Seq.choose (fun tfm ->
                        match tfmToSemVer tfm with
                        | Some v -> Some(tfm, v)
                        | None -> None)
                    |> Seq.sortBy (fun (_, v) -> v.major, v.minor)

                printfn $"choosing from among %A{tfmMap}"

                match tfmToSemVer tfm with
                | None ->
                    printfn "unable to parse target tfm, using latest"
                    Seq.last availableTFMs
                | Some ver ->
                    tfmMap
                    |> Seq.skipWhile (fun (_, v) -> (semver.compare (!!v, !!ver)) = enum -1) // skip while fsac tfm is less than target tfm
                    |> Seq.tryHead // get first fsac tfm that is greater than or equal to target tfm
                    |> Option.map fst
                    |> Option.defaultWith (fun () -> Seq.last availableTFMs)

            let probePathForTFMs (basePath: string) (tfm: string) =
                let availableTFMs =
                    node.fs.readdirSync (!!basePath) |> Seq.filter (fun p -> p.StartsWith "net") // there are loose files in the basePath, ignore those

                printfn $"Available FSAC TFMs: %A{availableTFMs}"

                if availableTFMs |> Seq.contains tfm then
                    printfn "TFM match found"
                    tfm, node.path.join (basePath, tfm, "fsautocomplete.dll")
                else
                    // find best-matching
                    let tfm = findBestTFM availableTFMs tfm
                    tfm, node.path.join (basePath, tfm, "fsautocomplete.dll")

            let isNetFolder (folder: string) =
                printfn $"checking folder %s{folder}"
                let baseName = node.path.basename folder

                baseName.StartsWith("net")
                && let stat = node.fs.statSync (!!folder) in
                   stat.isDirectory ()

            /// locates the FSAC dll and TFM for that dll given a host TFM
            let fsacPathForTfm (tfm: string) : string * string =
                match fsacNetcorePath with
                | null
                | "" ->
                    // user didn't specify a path, so use FSAC from our extension
                    let binPath = node.path.join (VSCodeExtension.ionidePluginPath (), "bin")
                    probePathForTFMs binPath tfm
                | userSpecified ->
                    if userSpecified.EndsWith ".dll" then
                        let tfm = node.path.basename (node.path.dirname userSpecified)
                        tfm, userSpecified
                    else
                        // if dir has tfm folders, probe
                        let filesAndFolders =
                            node.fs.readdirSync (!!userSpecified)
                            |> Seq.map (fun child -> node.path.join ([| userSpecified; child |]))

                        printfn $"candidates: %A{filesAndFolders}"

                        if filesAndFolders |> Seq.exists isNetFolder then
                            // tfm directories found, probe this directory like we would our own bin path
                            probePathForTFMs userSpecified tfm
                        else
                            // no tfm paths, try to use `fsautocomplete.dll` from this directory
                            let tfm = node.path.basename (node.path.dirname userSpecified)
                            tfm, node.path.join (userSpecified, "fsautocomplete.dll")

            let tfmForSdkVersion (v: SemVer) =
                match int v.major, int v.minor with
                | 3, 1 -> "netcoreapp3.1"
                | 3, 0 -> "netcoreapp3.0"
                | 2, 1 -> "netcoreapp2.1"
                | 2, 0 -> "netcoreapp2.0"
                | n, _ -> $"net{n}.0"

            let discoverDotnetArgs () =
                promise {

                    let! sdkVersionAtRootPath = sdkVersion ()

                    match sdkVersionAtRootPath with
                    | Error e ->
                        printfn $"Error finding dotnet version: {e}"
                        return failwith "Error finding dotnet version, do you have dotnet installed and on the PATH?"
                    | Ok sdkVersion ->
                        printfn "Parsed SDK version at root path: %s" sdkVersion.raw
                        let sdkTfm = tfmForSdkVersion sdkVersion
                        printfn "Parsed SDK version to tfm: %s" sdkTfm
                        let fsacTfm, fsacPath = fsacPathForTfm sdkTfm
                        printfn "Parsed TFM to fsac path: %s" fsacPath

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

                        let shouldApplyImplicitRollForward =
                            not (hasUserFxVersion || hasUserRollForward) && sdkTfm <> fsacTfm // if the SDK doesn't match one of our FSAC TFMs, then we're in compat mode

                        let args = userDotnetArgs

                        let envVariables =
                            [ if shouldApplyImplicitRollForward then
                                  "DOTNET_ROLL_FORWARD", box "LatestMajor"
                              match sdkVersion.prerelease with
                              | null -> ()
                              | pres when Seq.length pres > 0 -> "DOTNET_ROLL_FORWARD_TO_PRERELEASE", box 1
                              | _ -> () ]

                        return args, envVariables, fsacPath, sdkVersion
                }

            /// Converts true to 1 and false to 0
            /// Useful for environment variables that require this semantic
            let inline boolToInt b = if b then 1 else 0

            let spawnNetCore dotnet : JS.Promise<Executable> =
                promise {
                    let! (fsacDotnetArgs, fsacEnvVars, fsacPath, sdkVersion) = discoverDotnetArgs ()
                    // Only set DOTNET_GCHeapCount if we're on .NET 7 or higher
                    // .NET 6 has some issues with this env var on linux
                    // https://github.com/ionide/ionide-vscode-fsharp/issues/1899

                    let versionSupportingDATASGCMode = (semver.parse (Some(U2.Case1 "9.0.0"))).Value
                    let isdotnet8 = sdkVersion.major = 8

                    let isNet9orHigher =
                        semver.cmp (U2.Case2 sdkVersion, Operator.GTE, U2.Case2 versionSupportingDATASGCMode)

                    // datas is on by 9
                    let useDatas =
                        match gcServerUseDatas with
                        | Some b -> b
                        | None -> isNet9orHigher

                    let gcNoAffinitize =
                        match gcNoAffinitize with
                        | Some b -> b
                        | None -> isdotnet8 || not useDatas // no need to affinitize on 9 because Datas

                    let gcHeapCount =
                        match gcHeapCount with
                        | Some i -> Some i
                        | None -> if isdotnet8 then Some 2 else None

                    let fsacEnvVars =
                        [ yield! fsacEnvVars

                          if useDatas then
                              // DATAS and affinitization/heap management seem to be mutually exclusive, so we enforce that here.
                              yield "DOTNET_GCDynamicAdaptationMode", box (boolToInt useDatas) // https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#dynamic-adaptation-to-application-sizes-datas
                          else
                              // it doesn't really make sense to set GCNoAffinitize without setting GCHeapCount
                              yield "DOTNET_GCNoAffinitize", box (boolToInt gcNoAffinitize) // https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#affinitize

                              yield!
                                  gcHeapCount
                                  |> Option.map (fun hc -> "DOTNET_GCHeapCount", box (hc.ToString("X")))
                                  |> Option.toList // Requires hexadecimal value https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#heap-count

                          yield "DOTNET_GCConserveMemory", box gcConserveMemory //https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#conserve-memory
                          yield "DOTNET_GCServer", box (boolToInt gcServer) // https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#workstation-vs-server

                          if parallelReferenceResolution then
                              yield "FCS_ParallelReferenceResolution", box "true" ]

                    logger.Debug $"""FSAC (NETCORE): '%s{fsacPath}'"""

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

                    let additionalFSACArgs = "FSharp.fsac.fsacArgs" |> Configuration.get [||]

                    let args =
                        [ yield! fsacDotnetArgs
                          yield fsacPath
                          if fsacAttachDebugger then
                              yield "--attachdebugger"
                              yield "--wait-for-debugger"
                          if enableProjectGraph then
                              yield "--project-graph-enabled"
                          if openTelemetryEnabled then
                              yield "--otel-exporter-enabled"
                          if verbose then
                              yield "--verbose"
                          if fsacSilencedLogs <> null && fsacSilencedLogs.Length > 0 then
                              yield "--filter"
                              yield! fsacSilencedLogs
                          if useTransparentCompiler then
                              yield "--use-fcs-transparent-compiler"
                          match c.storageUri with
                          | Some uri ->
                              let storageDir = uri.fsPath
                              yield "--state-directory"
                              yield storageDir
                          | None -> ()
                          yield! additionalFSACArgs ]
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
            try
                let! startOpts = getOptions c
                logger.Debug("F# language server options: %%", startOpts)
                let cl = createClient startOpts
                registerCustomNotifications cl
                do! cl.start ()
                return ()
            with e ->
                logger.Error("Error starting F# language server: %%", e)
                return raise e
        }

    let stop () =
        promise {
            match client with
            | Some cl -> return! cl.stop ()
            | None -> return ()
        }

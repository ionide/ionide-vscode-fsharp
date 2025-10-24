module Ionide.VSCode.FSharp.TestExplorer

open System
open System.Text
open Fable.Core
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open Ionide.VSCode.FSharp.Import
open Ionide.VSCode.FSharp.Import.XmlDoc
open Fable.Core.JsInterop
open DTO
open Ionide.VSCode.Helpers

module node = Node.Api

let private lastOutput = Collections.Generic.Dictionary<string, string>()
let private outputChannel = window.createOutputChannel "F# - Test Adapter"
let private maxParallelTestProjects = 3

let private logger =
    ConsoleAndOutputChannelLogger(Some "TestExplorer", Level.DEBUG, Some outputChannel, Some Level.DEBUG)

module ArrayExt =

    let venn
        (leftIdf: 'Left -> 'Id)
        (rightIdf: 'Right -> 'Id)
        (left: 'Left array)
        (right: 'Right array)
        : ('Left array * ('Left * 'Right) array * 'Right array) =
        let leftIdMap =
            left
            |> Array.map (fun l -> (leftIdf l, l))
            |> dict
            |> Collections.Generic.Dictionary

        let rightIdMap =
            right
            |> Array.map (fun r -> (rightIdf r, r))
            |> dict
            |> Collections.Generic.Dictionary

        let leftIds = set leftIdMap.Keys
        let rightIds = set rightIdMap.Keys


        let intersection = Set.intersect leftIds rightIds

        let idToTuple id = (leftIdMap.[id], rightIdMap.[id])
        let intersectionPairs = intersection |> Array.ofSeq |> Array.map idToTuple

        let leftExclusiveIds = Set.difference leftIds intersection
        let rightExclusiveIds = Set.difference rightIds intersection

        let dictGet (dict: Collections.Generic.Dictionary<'Id, 'T>) id = dict.[id]
        let leftExclusive = leftExclusiveIds |> Array.ofSeq |> Array.map (dictGet leftIdMap)

        let rightExclusive =
            rightExclusiveIds |> Array.ofSeq |> Array.map (dictGet rightIdMap)

        (leftExclusive, intersectionPairs, rightExclusive)

    let mapKeepInput f col =
        col |> Array.map (fun input -> (input, f input))

module ListExt =
    let mapKeepInputAsync (f: 'a -> JS.Promise<'b>) col =
        col
        |> List.map (fun input ->
            promise {
                let! res = f input
                return (input, res)
            })

    let mapPartitioned f (left, right) =
        (left |> List.map f), (right |> List.map f)

module Dict =
    let tryGet (d: Collections.Generic.IDictionary<'key, 'value>) (key) : 'value option =
        if d.ContainsKey(key) then Some d[key] else None

module Option =

    let tee (f: 'a -> unit) (option: 'a option) =
        option |> Option.iter f
        option


module CancellationToken =
    let mergeTokens (tokens: CancellationToken list) =
        let tokenSource = vscode.CancellationTokenSource.Create()

        if tokens |> List.exists (fun t -> t.isCancellationRequested) then
            tokenSource.cancel ()
        else
            for t in tokens do
                t.onCancellationRequested.Invoke(fun _ ->
                    tokenSource.cancel ()
                    None)
                |> ignore

        tokenSource.token



type TestId = string
type ProjectPath = string
type TargetFramework = string

module Project =
    let testPathSubDir = ".ionide-test"
    let getOutputPaths () =
        let objOutputPath = node.path.join [|  "obj" ; testPathSubDir  ; node.path.sep|]
        let baseIntermediateOutputPath  = $"/p:BaseIntermediateOutputPath={objOutputPath}"
        let binOutputPath = node.path.join [|  "bin" ; testPathSubDir; node.path.sep |]
        let baseOutputPath  = $"/p:BaseOutputPath={binOutputPath}"
        [baseIntermediateOutputPath; baseOutputPath]

module ProjectPath =
    let inline ofString str = str

    let fromProject (project: Project) = project.Project

type FullTestName = string

module FullTestName =
    let inline ofString str = str

module TestName =
    let pathSeparator = '.'

    type Segment =
        { Text: string
          SeparatorBefore: string }

    module Segment =
        let empty = { Text = ""; SeparatorBefore = "" }

    let private segmentRegex = RegularExpressions.Regex(@"([+\.]?)([^+\.]+)")

    let splitSegments (fullTestName: FullTestName) =
        let matches =
            [ for x in segmentRegex.Matches(fullTestName) do
                  x ]

        matches
        |> List.map (fun m ->
            { Text = m.Groups[2].Value
              SeparatorBefore = m.Groups[1].Value })

    let appendSegment (parentPath: FullTestName) (segment: Segment) : FullTestName =
        $"{parentPath}{segment.SeparatorBefore}{segment.Text}"

    let fromPathAndTestName (classPath: string) (testName: string) : FullTestName =
        if classPath = "" then
            testName
        else
            $"{classPath}.{testName}"

    type private DataWithRelativePath<'t> =
        { data: 't; relativePath: Segment list }

    type NameHierarchy<'t> =
        { Data: 't option
          FullName: FullTestName
          Name: string
          Children: NameHierarchy<'t> array }

    module NameHierarchy =
        let tryPick (f: NameHierarchy<'t> -> Option<'u>) root =
            let rec recurse hierarchy =
                let searchResult = f hierarchy

                if Option.isSome searchResult then
                    searchResult
                else
                    hierarchy.Children |> Array.tryPick recurse

            recurse root

    let inferHierarchy (namedData: {| FullName: string; Data: 't |} array) : NameHierarchy<'t> array =

        let withRelativePath (named: {| FullName: string; Data: 't |}) =
            { data = named.Data
              relativePath = splitSegments named.FullName }

        let popTopPath data =
            { data with
                relativePath = data.relativePath.Tail }

        let rec recurse (parentPath: string) defsWithRelativePath : NameHierarchy<'t> array =
            let terminalNodes, intermediateNodes =
                defsWithRelativePath |> Array.partition (fun d -> d.relativePath.Length = 1)

            let mappedTerminals =
                terminalNodes
                |> Array.map (fun terminal ->
                    let segment = terminal.relativePath.Head

                    { Name = segment.Text
                      FullName = appendSegment parentPath segment
                      Data = Some terminal.data
                      Children = [||] })

            let mappedIntermediate =
                intermediateNodes
                |> Array.groupBy (fun d -> d.relativePath.Head)
                |> Array.map (fun (groupSegment, children) ->
                    let fullName = appendSegment parentPath groupSegment

                    { Name = groupSegment.Text
                      Data = None
                      FullName = appendSegment parentPath groupSegment
                      Children = recurse fullName (children |> Array.map popTopPath) })

            Array.concat [ mappedTerminals; mappedIntermediate ]


        namedData |> Array.map withRelativePath |> recurse ""

type TestItemCollection with

    member x.TestItems() : TestItem array =
        let arr = ResizeArray<TestItem>()
        x.forEach (fun t _ -> !! arr.Add(t))
        arr.ToArray()

type TestController with

    member x.TestItems() : TestItem array = x.items.TestItems()

type TestItem with

    member this.TestFramework: string = this?testFramework

[<RequireQualifiedAccess; StringEnum(CaseRules.None)>]
type TestResultOutcome =
    | NotExecuted
    | Failed
    | Passed
    | Skipped

module TestResultOutcome =
    let ofOutcomeDto (outcomeDto: TestOutcomeDTO) =
        match outcomeDto with
        | TestOutcomeDTO.Failed -> TestResultOutcome.Failed
        | TestOutcomeDTO.Passed -> TestResultOutcome.Passed
        | TestOutcomeDTO.Skipped -> TestResultOutcome.Skipped
        | TestOutcomeDTO.None -> TestResultOutcome.NotExecuted
        | TestOutcomeDTO.NotFound -> TestResultOutcome.NotExecuted
        | _ ->
            failwith
                $"Unknown value for TestOutcomeDTO: {outcomeDto}. The language server may have changed its possible values."


type TestFrameworkId = string

module TestFrameworkId =
    [<Literal>]
    let NUnit = "NUnit"

    [<Literal>]
    let MsTest = "MSTest"

    [<Literal>]
    let XUnit = "XUnit"

    [<Literal>]
    let Expecto = "Expecto"

    let tryFromExecutorUri adapterTypeName =
        if String.startWith "executor://nunit" adapterTypeName then
            Some NUnit
        else if String.startWith "executor://mstest" adapterTypeName then
            Some MsTest
        else if String.startWith "executor://xunit" adapterTypeName then
            Some XUnit
        else if String.startWith "executor://yolodev" adapterTypeName then
            Some Expecto
        else
            None

module TestItemDTO =
    let getFullname_withNestedParamTests (dto: TestItemDTO) =
        match dto.ExecutorUri |> TestFrameworkId.tryFromExecutorUri with
        // NOTE: XUnit and MSTest don't include the theory case parameters in the FullyQualifiedName, but do include them in the DisplayName.
        //       Thus we need to append the DisplayName to differentiate the test cases
        | Some TestFrameworkId.MsTest ->
            if dto.FullName.EndsWith(dto.DisplayName) then
                dto.FullName
            else
                dto.FullName + "." + dto.DisplayName
        | Some TestFrameworkId.XUnit ->
            // NOTE: XUnit includes the FullyQualifiedName in the DisplayName.
            //       But it doesn't nest theory cases, just appends the case parameters
            if dto.DisplayName <> dto.FullName then
                let theoryCaseFragment = dto.DisplayName.Split('.') |> Array.last
                dto.FullName + "." + theoryCaseFragment
            else
                dto.FullName
        | _ -> dto.FullName

type TestResult =
    { FullTestName: string
      Outcome: TestResultOutcome
      Output: string option
      ErrorMessage: string option
      ErrorStackTrace: string option
      Expected: string option
      Actual: string option
      Timing: float
      TestFramework: TestFrameworkId option
      ProjectFilePath: ProjectFilePath
      TargetFramework: TargetFramework }

module TestResult =
    let tryExtractExpectedAndActual (message: string option) =
        let expected, actual =
            match message with
            | None -> None, None
            | Some message ->
                let lines =
                    message.Split([| "\r\n"; "\n" |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (fun n -> n.TrimStart())

                let tryFind (startsWith: string) =
                    Array.tryFind (fun (line: string) -> line.StartsWith(startsWith)) lines
                    |> Option.map (fun line -> line.Replace(startsWith, "").TrimStart())

                tryFind "Expected:", tryFind "But was:" |> Option.orElse (tryFind "Actual:")

        expected, actual

    let ofTestResultDTO (testResultDto: TestResultDTO) : TestResult =
        let expected, actual = tryExtractExpectedAndActual testResultDto.ErrorMessage

        { FullTestName = testResultDto.TestItem |> TestItemDTO.getFullname_withNestedParamTests
          Outcome = testResultDto.Outcome |> TestResultOutcome.ofOutcomeDto
          Output = testResultDto.AdditionalOutput
          ErrorMessage = testResultDto.ErrorMessage
          ErrorStackTrace = testResultDto.ErrorStackTrace
          Timing = testResultDto.Duration.Milliseconds
          TestFramework = testResultDto.TestItem.ExecutorUri |> TestFrameworkId.tryFromExecutorUri
          Expected = expected
          Actual = actual
          ProjectFilePath = testResultDto.TestItem.ProjectFilePath
          TargetFramework = testResultDto.TestItem.TargetFramework }

module Path =

    let tryPath (path: string) =
        if node.fs.existsSync (U2.Case1 path) then
            Some path
        else
            None

    let deleteIfExists (path: string) =
        if node.fs.existsSync (U2.Case1 path) then
            node.fs.unlinkSync (!^path)

    let getNameOnly (path: string) =
        node.path.basename (path, node.path.extname (path))

    let split (path: string) : string array =
        path.Split([| node.path.sep |], StringSplitOptions.RemoveEmptyEntries)

    let private join segments = node.path.join (segments)

    let removeSpecialRelativeSegments (path: string) : string =
        let specialSegments = set [ ".."; "." ]
        path |> split |> Array.skipWhile specialSegments.Contains |> join



module TrxParser =

    type Execution = { Id: string }

    type TestMethod =
        { AdapterTypeName: string
          ClassName: string
          Name: string }

    type UnitTest =
        { Name: string
          Execution: Execution
          TestMethod: TestMethod }

        member self.FullName =
            // IMPORTANT: XUnit and MSTest don't include the parameterized test case data in the TestMethod.Name
            //    but NUnit and MSTest don't use fully qualified names in UnitTest.Name.
            //    Therefore, we have to conditionally build this full name based on the framework
            match self.TestMethod.AdapterTypeName |> TestFrameworkId.tryFromExecutorUri with
            | Some TestFrameworkId.NUnit -> TestName.fromPathAndTestName self.TestMethod.ClassName self.TestMethod.Name
            | Some TestFrameworkId.MsTest -> TestName.fromPathAndTestName self.TestMethod.ClassName self.Name
            | _ -> self.Name

    type ErrorInfo =
        { Message: string option
          StackTrace: string option }

    type Output =
        { StdOut: string option
          ErrorInfo: ErrorInfo }

    type UnitTestResult =
        { ExecutionId: string
          Outcome: string
          Duration: TimeSpan
          Output: Output }

    type TestWithResult =
        { UnitTest: UnitTest
          UnitTestResult: UnitTestResult }

    let makeTrxPath (workspaceRoot: string) (storageFolderPath: string) (projectPath: ProjectFilePath) : string =
        let relativeProjectPath = node.path.relative (workspaceRoot, projectPath)
        let projectName = Path.getNameOnly projectPath

        let relativeResultsPath =
            relativeProjectPath |> Path.removeSpecialRelativeSegments |> node.path.dirname

        let trxPath =
            node.path.resolve (storageFolderPath, "TestResults", relativeResultsPath, $"{projectName}.trx")

        trxPath

    let trxSelector (trxPath: string) : XPath.XPathSelector =
        let trxContent = node.fs.readFileSync (trxPath, "utf8")
        let xmlDoc = mkDoc trxContent
        XPath.XPathSelector(xmlDoc, "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")

    let extractTestDefinitionsFromSelector (xpathSelector: XPath.XPathSelector) : UnitTest array =
        let extractTestDef (node: XmlNode) : UnitTest =
            let executionId = xpathSelector.SelectStringRelative(node, "t:Execution/@id")

            // IMPORTANT: t:UnitTest/@name is not the same as t:TestMethod/@className + t:TestMethod/@name
            //  for theory tests in xUnit and MSTest https://github.com/ionide/ionide-vscode-fsharp/issues/1935
            let fullTestName = xpathSelector.SelectStringRelative(node, "@name")
            let className = xpathSelector.SelectStringRelative(node, "t:TestMethod/@className")
            let testMethodName = xpathSelector.SelectStringRelative(node, "t:TestMethod/@name")

            let testAdapter =
                xpathSelector.SelectStringRelative(node, "t:TestMethod/@adapterTypeName")

            { Name = fullTestName
              Execution = { Id = executionId }
              TestMethod =
                { Name = testMethodName
                  ClassName = className
                  AdapterTypeName = testAdapter } }

        xpathSelector.SelectNodes "/t:TestRun/t:TestDefinitions/t:UnitTest"
        |> Array.map extractTestDef

    let extractTestDefinitions (trxPath: string) =
        let selector = trxSelector trxPath
        extractTestDefinitionsFromSelector selector


    let extractResultsSection (xpathSelector: XPath.XPathSelector) : UnitTestResult array =
        let extractRow (node: XmlNode) : UnitTestResult =

            let executionId = xpathSelector.SelectStringRelative(node, "@executionId")

            let outcome = xpathSelector.SelectStringRelative(node, "@outcome")

            let outputMessage = xpathSelector.TrySelectStringRelative(node, "t:Output/t:StdOut")

            let errorInfoMessage =
                xpathSelector.TrySelectStringRelative(node, "t:Output/t:ErrorInfo/t:Message")

            let errorStackTrace =
                xpathSelector.TrySelectStringRelative(node, "t:Output/t:ErrorInfo/t:StackTrace")

            let durationSpan =
                let durationString = xpathSelector.SelectStringRelative(node, "@duration")
                let success, ts = TimeSpan.TryParse(durationString)
                if success then ts else TimeSpan.Zero

            { ExecutionId = executionId
              Outcome = outcome
              Duration = durationSpan
              Output =
                { StdOut = outputMessage
                  ErrorInfo =
                    { StackTrace = errorStackTrace
                      Message = errorInfoMessage } } }

        xpathSelector.SelectNodes "/t:TestRun/t:Results/t:UnitTestResult"
        |> Array.map extractRow



    let extractTrxResults (trxPath: string) =
        let xpathSelector = trxSelector trxPath

        let trxDefs = extractTestDefinitionsFromSelector xpathSelector

        let trxResults = extractResultsSection xpathSelector

        let trxDefId (testDef: UnitTest) = testDef.Execution.Id
        let trxResId (res: UnitTestResult) = res.ExecutionId
        let _, matched, _ = ArrayExt.venn trxDefId trxResId trxDefs trxResults

        let matchedToResult (testDef: UnitTest, testResult: UnitTestResult) : TestWithResult =
            { UnitTest = testDef
              UnitTestResult = testResult }

        let normalizedResults = matched |> Array.map matchedToResult
        normalizedResults


    let inferHierarchy (testDefs: UnitTest array) : TestName.NameHierarchy<UnitTest> array =
        testDefs
        |> Array.map (fun td -> {| FullName = td.FullName; Data = td |})
        |> TestName.inferHierarchy


module VSCodeActions =
    let launchDebugger (processId: string) =
        let launchRequest: DebugConfiguration =
            {| name = ".NET Core Attach"
               ``type`` = "coreclr"
               request = "attach"
               processId = processId |}
            |> box
            |> unbox

        let folder = workspace.workspaceFolders.Value.[0]

        Vscode.debug.startDebugging (Some folder, U2.Case2 launchRequest)
        |> Promise.ofThenable


module DotnetCli =
    type StandardOutput = string
    type StandardError = string

    module Process =
        open Ionide.VSCode.Helpers.CrossSpawn
        open Ionide.VSCode.Helpers.Process
        open Node.ChildProcess

        let private cancelErrorMessage = "SIGINT"

        /// <summary>
        /// Fire off a command and gather the error, if any, and the stdout and stderr streams.
        /// The command is fired from the workspace's root path.
        /// </summary>
        /// <param name="command">the 'base' command to execute</param>
        /// <param name="args">an array of additional CLI args</param>
        /// <returns></returns>
        let execWithCancel
            command
            args
            (env: obj option)
            (outputCallback: Node.Buffer.Buffer -> unit)
            (cancellationToken: CancellationToken)
            : JS.Promise<ExecError option * string * string> =

            if not cancellationToken.isCancellationRequested then
                let options = createEmpty<ExecOptions>
                options.cwd <- workspace.rootPath
                env |> Option.iter (fun env -> options?env <- env)

                Promise.create (fun resolve reject ->
                    let stdout = ResizeArray()
                    let stderr = ResizeArray()
                    let mutable error = None

                    let childProcess =
                        crossSpawn.spawn (command, args, options = options)
                        |> onOutput (fun e ->
                            outputCallback e
                            stdout.Add(string e))
                        |> onError (fun e -> error <- Some e)
                        |> onErrorOutput (fun e -> stderr.Add(string e))
                        |> onClose (fun code signal ->
                            resolve (unbox error, String.concat "\n" stdout, String.concat "\n" stderr))

                    cancellationToken.onCancellationRequested.Invoke(fun _ ->
                        childProcess.kill (cancelErrorMessage)
                        None)
                    |> ignore

                )
            else
                promise { return (None, "", "") }


    let restore
        (projectPath: string)
        args
        : JS.Promise<Node.ChildProcess.ExecError option * StandardOutput * StandardError> =
        Process.exec "dotnet" (ResizeArray([| "restore"; projectPath; yield! args |]))

    let private debugProcessIdRegex = RegularExpressions.Regex(@"Process Id: (.*),")

    let private tryGetDebugProcessId consoleOutput =
        let m = debugProcessIdRegex.Match(consoleOutput)

        if m.Success then
            let processId = m.Groups.[1].Value
            Some processId
        else
            None

    type DebugTests =
        | Debug
        | NoDebug

    module DebugTests =
        let ofBool bool = if bool then Debug else NoDebug

    let private dotnetTest
        (cancellationToken: CancellationToken)
        (projectPath: string)
        (targetFramework: string)
        (trxOutputPath: string option)
        (shouldDebug: DebugTests)
        (additionalArgs: string array)
        : JS.Promise<Node.ChildProcess.ExecError option * StandardOutput * StandardError> =

        let args =
            [| "test"
               $"\"{projectPath}\""
               $"--framework:\"{targetFramework}\""
               if Option.isSome trxOutputPath then
                   $"--logger:\"trx;LogFileName={trxOutputPath.Value}\""
               "--noLogo"
               yield! additionalArgs |]

        let argString = String.Join(" ", args)
        logger.Debug($"Running `dotnet {argString}`")

        let getEnv enableTestHostDebugger =
            let parentEnv = Node.Api.``process``.env
            let childEnv = parentEnv
            //NOTE: Important to include VSTEST_HOST_DEBUG=0 when not debugging to remove stale values
            //      that may cause the debugger to wait and hang
            if enableTestHostDebugger then
                childEnv?VSTEST_HOST_DEBUG <- 1
                childEnv?VSTEST_DEBUG_NOBP <- 1
            else
                childEnv?VSTEST_HOST_DEBUG <- 0
                childEnv?VSTEST_DEBUG_NOBP <- 0

            childEnv |> box |> Some

        match shouldDebug with
        | Debug ->
            let mutable isDebuggerStarted = false

            let tryLaunchDebugger (consoleOutput: Node.Buffer.Buffer) =
                if not isDebuggerStarted then
                    // NOTE: the processId we need to attach to is not the one we started for `dotnet test`.
                    //       Dotnet test will return the correct process id if (and only if) we are in debug mode
                    match tryGetDebugProcessId (string consoleOutput) with
                    | None -> ()
                    | Some processId ->
                        VSCodeActions.launchDebugger processId |> ignore
                        isDebuggerStarted <- true

            Process.execWithCancel "dotnet" (ResizeArray(args)) (getEnv true) tryLaunchDebugger cancellationToken
        | NoDebug -> Process.execWithCancel "dotnet" (ResizeArray(args)) (getEnv false) ignore cancellationToken

    type TrxPath = string
    type ConsoleOutput = string

    let test
        (projectPath: string)
        (targetFramework: string)
        (trxOutputPath: string option)
        (filterExpression: string option)
        (shouldDebug: DebugTests)
        (cancellationToken: CancellationToken)
        : JS.Promise<ConsoleOutput> =
        promise {
            // https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-test#filter-option-details

            let filter =
                match filterExpression with
                | None -> Array.empty
                | Some filterExpression -> [| "--filter"; $"\"{filterExpression}\"" |]

            if filter.Length > 0 then
                logger.Debug("Filter", filter)

            let! errored, stdOutput, stdError =
                dotnetTest
                    cancellationToken
                    projectPath
                    targetFramework
                    trxOutputPath
                    shouldDebug
                    [| "--no-build"; yield! filter |]

            match errored with
            | Some error -> logger.Error("Test run failed - %s - %s - %s", error, stdOutput, stdError)
            | None -> logger.Debug("Test run exitCode - %s - %s", stdOutput, stdError)

            return (stdOutput + stdError)
        }

    let listTests projectPath targetFramework (shouldBuild: bool) (cancellationToken: CancellationToken) =
        let splitLines (str: string) =
            str.Split([| "\r\n"; "\n\r"; "\n" |], StringSplitOptions.RemoveEmptyEntries)

        promise {
            let additionalArgs = if not shouldBuild then [| "--no-build" |] else Array.empty

            let basePathArgs  = Project.getOutputPaths ()

            let! _, stdOutput, _ =
                dotnetTest
                    cancellationToken
                    projectPath
                    targetFramework
                    None
                    NoDebug
                    [| "--list-tests"; yield! additionalArgs; yield! basePathArgs; "/p:BuildProjectReferences=false" |]

            let testNames =
                stdOutput
                |> splitLines
                |> Array.skipWhile (((<>) << String.trim) "The following Tests are available:")
                |> Array.safeSkip 1
                |> Array.choose (fun line ->
                    let line = line.TrimStart()

                    if (not << String.IsNullOrEmpty) line then
                        Some line
                    else
                        None)

            return testNames
        }

type LocationRecord =
    { Uri: Uri; Range: Vscode.Range option }

module LocationRecord =
    let tryGetUri (l: LocationRecord option) = l |> Option.map (fun l -> l.Uri)
    let tryGetRange (l: LocationRecord option) = l |> Option.bind (fun l -> l.Range)

    let testToLocation (testItem: TestItem) =
        match testItem.uri with
        | None -> None
        | Some uri -> Some { Uri = uri; Range = testItem.range }

type CodeLocationCache() =
    let locationCache = Collections.Generic.Dictionary<TestId, LocationRecord>()

    member _.Save(testId: TestId, location: LocationRecord) = locationCache[testId] <- location

    member _.GetById(testId: TestId) = locationCache.TryGet testId

    member _.GetKnownTestIds() : TestId seq = locationCache.Keys

    member _.DeleteByFile(uri: Uri) =
        for kvp in locationCache do
            if kvp.Value.Uri.fsPath = uri.fsPath then
                locationCache.Remove(kvp.Key) |> ignore


module TestItem =

    let private idSeparator = " -- "

    let constructId (projectPath: ProjectPath) (fullName: FullTestName) : TestId =
        String.Join(idSeparator, [| projectPath; fullName |])

    let constructProjectRootId (projectPath: ProjectPath) : TestId = constructId projectPath ""

    let private componentizeId (testId: TestId) : (ProjectPath * FullTestName) =
        // IMPORTANT: the fullname should be last and we should limit the number of substrings
        //            to prevent incorrently splitting tests names with -- in them
        let split =
            testId.Split(separator = [| idSeparator |], count = 2, options = StringSplitOptions.None)

        (split.[0], split.[1])

    let getFullName (testId: TestId) : FullTestName =
        let _, fullName = componentizeId testId
        fullName

    let getProjectPath (testId: TestId) : ProjectPath =
        let projectPath, _ = componentizeId testId
        projectPath

    let getId (t: TestItem) = t.id

    let tryPick (f: TestItem -> Option<'u>) root =
        let rec recurse testItem =
            let searchResult = f testItem

            if Option.isSome searchResult then
                searchResult
            else
                testItem.children.TestItems() |> Array.tryPick recurse

        recurse root

    let runnableChildren (root: TestItem) : TestItem array =
        // The goal is to collect here the actual runnable tests, they might be nested under a tree structure.
        let rec visit (testItem: TestItem) : TestItem array =
            if testItem.children.size = 0. then
                [| testItem |]
            else
                testItem.children.TestItems() |> Array.collect visit

        visit root

    let runnableFromArray (testCollection: TestItem array) : TestItem array =
        testCollection
        |> Array.collect runnableChildren
        // NOTE: there can be duplicates. i.e. if a child and parent are both selected in the explorer
        |> Array.distinctBy getId

    let tryGetLocation (testItem: TestItem) =
        match testItem.uri, testItem.range with
        | Some uri, Some range -> Some(vscode.Location.Create(uri, !^range))
        | _ -> None

    let preWalk f (root: TestItem) =
        let rec recurse (t: TestItem) =
            let mapped = f t
            let mappedChildren = t.children.TestItems() |> Array.collect recurse
            Array.concat [ [| mapped |]; mappedChildren ]

        recurse root

    type TestItemBuilder =
        { id: TestId
          label: string
          uri: Uri option
          range: Vscode.Range option
          children: TestItem array
          // i.e. NUnit. Used for an Nunit-specific workaround
          testFramework: TestFrameworkId option }

    type TestItemFactory = TestItemBuilder -> TestItem

    let itemFactoryForController (testController: TestController) =
        let factory builder =
            let testItem =
                match builder.uri with
                | Some uri -> testController.createTestItem (builder.id, builder.label, uri)
                | None -> testController.createTestItem (builder.id, builder.label)

            builder.children |> Array.iter testItem.children.add
            testItem.range <- builder.range

            match builder.testFramework with
            | Some frameworkId -> testItem?testFramework <- frameworkId
            | None -> ()

            testItem

        factory


    let fromNamedHierarchy
        (itemFactory: TestItemFactory)
        (tryGetLocation: TestId -> LocationRecord option)
        projectPath
        (hierarchy: TestName.NameHierarchy<'t>)
        : TestItem =
        let rec recurse (namedNode: TestName.NameHierarchy<'t>) =
            let id = constructId projectPath namedNode.FullName
            let location = tryGetLocation id

            itemFactory
                { id = id
                  label = namedNode.Name
                  uri = location |> LocationRecord.tryGetUri
                  range = location |> LocationRecord.tryGetRange
                  children = namedNode.Children |> Array.map recurse
                  testFramework = None }

        recurse hierarchy

    let fromTestAdapter
        (itemFactory: TestItemFactory)
        (uri: Uri)
        (projectPath: ProjectPath)
        (t: TestAdapterEntry)
        : TestItem =
        let getNameSeparator parentModuleType moduleType =
            match parentModuleType, moduleType with
            | None, _ -> ""
            | Some "NoneModule", _
            | Some _, "NoneModule" -> "."
            | _ -> "+"

        let rec recurse (parentFullName: FullTestName) (parentModuleType: string option) (t: TestAdapterEntry) =
            let fullName =
                parentFullName + (getNameSeparator parentModuleType t.moduleType) + t.name

            let range =
                Some(
                    vscode.Range.Create(
                        vscode.Position.Create(t.range.start.line, t.range.start.character),
                        vscode.Position.Create(t.range.``end``.line, t.range.``end``.character)
                    )
                )

            let ti =
                itemFactory
                    { id = constructId projectPath fullName
                      label = t.name
                      uri = Some uri
                      range = range
                      children = t.childs |> Array.map (fun n -> recurse fullName (Some t.moduleType) n)
                      testFramework = t?``type`` }

            ti

        recurse "" None t

    let fromProject
        (testItemFactory: TestItemFactory)
        (projectPath: ProjectPath)
        (targetFramework: TargetFramework)
        (children: TestItem array)
        : TestItem =
        testItemFactory
            { id = constructProjectRootId projectPath
              label = $"{Path.getNameOnly projectPath} ({targetFramework})"
              uri = None
              range = None
              children = children
              testFramework = None }


    let ofTestDTOs testItemFactory tryGetLocation (flatTests: TestItemDTO array) =

        let fromTestItemDTO
            (constructId: FullTestName -> TestId)
            (itemFactory: TestItemFactory)
            (tryGetLocation: TestId -> LocationRecord option)
            (hierarchy: TestName.NameHierarchy<TestItemDTO>)
            : TestItem =
            let toUri path =
                try
                    if String.IsNullOrEmpty path then
                        None
                    else
                        vscode.Uri.parse ($"file:///{path}", true) |> Some
                with e ->
                    logger.Debug($"Failed to parse test location uri {path}", e)
                    None

            let toRange (rangeDto: TestFileRange) =
                vscode.Range.Create(
                    vscode.Position.Create(rangeDto.StartLine, 0),
                    vscode.Position.Create(rangeDto.EndLine, 0)
                )

            let tryDtoToLocation (dto: TestItemDTO) : LocationRecord option =
                match dto.CodeFilePath |> Option.bind toUri, dto.CodeLocationRange with
                | Some path, Some range ->
                    { Uri = path
                      Range = toRange (range) |> Some }
                    |> Some
                | _ -> None

            let rec recurse (namedNode: TestName.NameHierarchy<TestItemDTO>) =
                let id = constructId namedNode.FullName

                let codeLocation =
                    namedNode.Data
                    |> Option.bind tryDtoToLocation
                    |> Option.orElseWith (fun _ -> tryGetLocation id)

                itemFactory
                    { id = id
                      label = namedNode.Name
                      uri = codeLocation |> LocationRecord.tryGetUri
                      range = codeLocation |> LocationRecord.tryGetRange
                      children = namedNode.Children |> Array.map recurse
                      testFramework =
                        namedNode.Data
                        |> Option.bind (fun t -> t.ExecutorUri |> TestFrameworkId.tryFromExecutorUri) }

            recurse hierarchy

        let mapDtosForProject ((projectPath, targetFramework), flatTests) =
            let testDtoToNamedItem (dto: TestItemDTO) =
                {| Data = dto
                   FullName = dto |> TestItemDTO.getFullname_withNestedParamTests |}

            let namedHierarchies =
                flatTests |> Array.map testDtoToNamedItem |> TestName.inferHierarchy

            let projectChildTestItems =
                namedHierarchies
                |> Array.map (fromTestItemDTO (constructId projectPath) testItemFactory tryGetLocation)

            fromProject testItemFactory projectPath targetFramework projectChildTestItems

        let testDtosByProject =
            flatTests |> Array.groupBy (fun dto -> dto.ProjectFilePath, dto.TargetFramework)

        let testItemsByProject = testDtosByProject |> Array.map mapDtosForProject

        testItemsByProject


    let isProjectItem (testId: TestId) =
        constructProjectRootId (getProjectPath testId) = testId


    let tryFromTestForFile (testItemFactory: TestItemFactory) (testsForFile: TestForFile) =
        let fileUri = vscode.Uri.parse (testsForFile.file, true)

        Project.tryFindLoadedProjectByFile fileUri.fsPath
        |> Option.map (fun project ->
            let projectPath = ProjectPath.ofString project.Project

            let fileTests =
                testsForFile.tests
                |> Array.map (fromTestAdapter testItemFactory fileUri projectPath)

            [| fromProject testItemFactory projectPath project.Info.TargetFramework fileTests |])

    let tryGetById (testId: TestId) (rootCollection: TestItem array) : TestItem option =
        let projectPath, fullTestName = componentizeId testId

        let rec recurse
            (collection: TestItemCollection)
            (parentPath: FullTestName)
            (remainingPath: TestName.Segment list)
            =

            let currentLabel, remainingPath =
                match remainingPath with
                | currentLabel :: remainingPath -> (currentLabel, remainingPath)
                | [] -> TestName.Segment.empty, []

            let fullName = TestName.appendSegment parentPath currentLabel
            let id = constructId projectPath fullName
            let existingItem = collection.get (id)

            match existingItem with
            | None -> None
            | Some existingItem ->
                if remainingPath <> [] then
                    recurse existingItem.children fullName remainingPath
                else
                    Some existingItem


        let pathSegments = TestName.splitSegments fullTestName

        rootCollection
        |> Array.tryFind (fun ti -> ti.id = constructProjectRootId projectPath)
        |> Option.bind (fun projectRoot -> recurse projectRoot.children "" pathSegments)


    let getOrMakeHierarchyPath
        (rootCollection: TestItemCollection)
        (itemFactory: TestItemFactory)
        (tryGetLocation: TestId -> LocationRecord option)
        (projectPath: ProjectPath)
        (targetFramework: TargetFramework)
        (fullTestName: FullTestName)
        =
        let rec recurse
            (collection: TestItemCollection)
            (parentPath: FullTestName)
            (remainingPath: TestName.Segment list)
            =

            let currentLabel, remainingPath =
                match remainingPath with
                | currentLabel :: remainingPath -> (currentLabel, remainingPath)
                | [] -> TestName.Segment.empty, []

            let fullName = TestName.appendSegment parentPath currentLabel
            let id = constructId projectPath fullName
            let maybeLocation = tryGetLocation id
            let existingItem = collection.get (id)

            let testItem =
                match existingItem with
                | Some existing -> existing
                | None ->
                    itemFactory
                        { id = id
                          label = currentLabel.Text
                          uri = maybeLocation |> LocationRecord.tryGetUri
                          range = maybeLocation |> LocationRecord.tryGetRange
                          children = [||]
                          testFramework = None }

            collection.add (testItem)

            if remainingPath <> [] then
                recurse testItem.children fullName remainingPath
            else
                testItem

        let getOrMakeProjectRoot projectPath targetFramework =
            match rootCollection.get (constructProjectRootId projectPath) with
            | None -> fromProject itemFactory projectPath targetFramework [||]
            | Some projectTestItem -> projectTestItem

        let projectRoot = getOrMakeProjectRoot projectPath targetFramework

        let pathSegments = TestName.splitSegments fullTestName
        recurse projectRoot.children "" pathSegments


module CodeLocationCache =

    let cacheTestLocations (locationCache: CodeLocationCache) (filePath: string) (testItems: TestItem array) =
        let fileUri = vscode.Uri.parse (filePath, true)
        locationCache.DeleteByFile(fileUri)


        let saveTestItem (testItem: TestItem) =
            LocationRecord.testToLocation testItem
            |> Option.iter (fun l -> locationCache.Save(testItem.id, l))

        testItems |> Array.map (TestItem.preWalk saveTestItem) |> ignore


module ProjectExt =
    let getAllWorkspaceProjects () =
        let getPath (status: Project.ProjectLoadingState) =
            match status with
            | Project.ProjectLoadingState.Loaded p -> p.Project
            | Project.ProjectLoadingState.LanguageNotSupported path -> path
            | Project.ProjectLoadingState.Loading path -> path
            | Project.ProjectLoadingState.Failed(path, _) -> path
            | Project.ProjectLoadingState.NotRestored(path, _) -> path

        Project.getInWorkspace () |> List.map getPath

    let isTestProject (project: Project) =
        let testProjectIndicators =
            set [ "Microsoft.TestPlatform.TestHost"; "Microsoft.NET.Test.Sdk" ]

        project.PackageReferences
        |> Array.exists (fun pr -> Set.contains pr.Name testProjectIndicators)


type CodeBasedTestId = TestId
type ResultBasedTestId = TestId


module TestDiscovery =


    let tryMatchCodeLocations
        (testItemFactory: TestItem.TestItemFactory)
        (tryMatchDisplacedTest: TestId -> TestItem option)
        (rootTestCollection: TestItemCollection)
        (testsFromCode: TestItem array)
        =
        let cloneWithUri (target: TestItem, withUri: TestItem) =
            let replacementItem =
                testItemFactory
                    { id = target.id
                      label = target.label
                      uri = withUri.uri
                      range = withUri.range
                      children = target.children.TestItems()
                      testFramework = withUri?testFramework }

            (replacementItem, withUri)

        let rec recurse (target: TestItemCollection) (withUri: TestItem array) : unit =
            let treeOnly, matched, _codeOnly =
                ArrayExt.venn TestItem.getId TestItem.getId (target.TestItems()) withUri

            let exactPathMatch = matched |> Array.map cloneWithUri

            let advancedMatchAttempted =
                treeOnly
                |> Array.map (fun unlocated ->
                    match tryMatchDisplacedTest unlocated.id with
                    | Some displacedFragmentRoot ->
                        let updated, _ = cloneWithUri (unlocated, displacedFragmentRoot)
                        recurse updated.children (displacedFragmentRoot.children.TestItems())
                        updated
                    | None -> unlocated)

            let newTestCollection =
                Array.concat [ advancedMatchAttempted; exactPathMatch |> Array.map fst ]

            target.replace (ResizeArray newTestCollection)

            exactPathMatch
            |> Array.iter (fun (target, withUri) -> recurse target.children (withUri.children.TestItems()))

        recurse rootTestCollection testsFromCode

    let mergeCodeUpdates
        (targetCollection: TestItemCollection)
        (previousCodeTests: TestItem array)
        (newCodeTests: TestItem array)
        =
        let rangeComparable (maybeRange: Vscode.Range option) =
            let positionComparable (p: Vscode.Position) = $"{p.line}:{p.character}"

            match maybeRange with
            | None -> "none"
            | Some range -> $"({positionComparable range.start},{positionComparable range.``end``})"

        let rec recurse
            (targetCollection: TestItemCollection)
            (previousCodeTests: TestItem array)
            (newCodeTests: TestItem array)
            =
            let comparef (t: TestItem) = (t.id, rangeComparable t.range)

            let removed, unchanged, added =
                ArrayExt.venn comparef comparef previousCodeTests newCodeTests

            removed |> Array.map TestItem.getId |> Array.iter targetCollection.delete

            added |> Array.iter targetCollection.add

            unchanged
            |> Array.iter (fun (previousCodeTest, newCodeTest) ->
                match targetCollection.get newCodeTest.id with
                | None -> ()
                | Some targetItem ->
                    recurse
                        targetItem.children
                        (previousCodeTest.children.TestItems())
                        (newCodeTest.children.TestItems()))

        recurse targetCollection previousCodeTests newCodeTests

    let discoverFromTrx
        testItemFactory
        (tryGetLocation: TestId -> LocationRecord option)
        makeTrxPath
        (projects: Project list)
        =

        let testProjects =
            projects
            |> Array.ofList
            |> Array.choose (fun p ->
                match p.Project |> makeTrxPath |> Path.tryPath with
                | Some trxPath -> Some(p, trxPath)
                | None -> None)

        let trxTestsPerProject =
            testProjects
            |> Array.map (fun (p, trxPath) -> (p, TrxParser.extractTestDefinitions trxPath))

        let treeItems =
            trxTestsPerProject
            |> Array.map (fun (project, trxDefs) ->
                let projectPath = ProjectPath.ofString project.Project
                let hierarchy = TrxParser.inferHierarchy trxDefs

                let fromTrxDef (hierarchy: TestName.NameHierarchy<TrxParser.UnitTest>) =
                    // NOTE: A project could have multiple test frameworks, but we only track NUnit for now to work around a defect
                    //       The complexity of modifying inferHierarchy and fromNamedHierarchy to distinguish frameworks for individual chains seems excessive for current needs
                    //       Thus, this just determins if there are *any* Nunit tests in the project and treats all the tests like NUnit tests if there are.
                    let testFramework =
                        TestName.NameHierarchy.tryPick
                            (fun nh ->
                                nh.Data
                                |> Option.bind (fun (trxDef: TrxParser.UnitTest) ->
                                    TestFrameworkId.tryFromExecutorUri trxDef.TestMethod.AdapterTypeName))
                            hierarchy

                    let testItemFactory (testItemBuilder: TestItem.TestItemBuilder) =
                        testItemFactory
                            { testItemBuilder with
                                testFramework = testFramework }

                    TestItem.fromNamedHierarchy testItemFactory tryGetLocation projectPath hierarchy

                let projectTests = hierarchy |> Array.map fromTrxDef

                TestItem.fromProject testItemFactory projectPath project.Info.TargetFramework projectTests)

        treeItems

    let private tryInferTestFrameworkFromPackage (project: Project) =

        let detectablePackageToFramework =
            dict
                [ "Expecto", TestFrameworkId.Expecto
                  "xunit.abstractions", TestFrameworkId.XUnit ]

        let getPackageName (pr: PackageReference) = pr.Name

        project.PackageReferences
        |> Array.tryPick (getPackageName >> Dict.tryGet detectablePackageToFramework)

    /// Does this project use a test framework where we can consistently discover test cases using `dotnet test --list-tests`
    /// This requires the test library to print the fully-qualified test names
    let canListTestCasesWithCli (project: Project) =
        let librariesCapableOfListOnlyDiscovery =
            set [ TestFrameworkId.Expecto; TestFrameworkId.XUnit ]

        tryInferTestFrameworkFromPackage project
        |> Option.map librariesCapableOfListOnlyDiscovery.Contains
        |> Option.defaultValue false


    /// Use `dotnet test --list-tests` to
    let discoverTestsByCliListTests testItemFactory tryGetLocation cancellationToken (project: Project) =
        promise {

            let! testNames = DotnetCli.listTests project.Project project.Info.TargetFramework false cancellationToken

            let detectedTestFramework = tryInferTestFrameworkFromPackage project

            let testItemFactory (testItemBuilder: TestItem.TestItemBuilder) =
                testItemFactory
                    { testItemBuilder with
                        testFramework = detectedTestFramework }

            let testHierarchy =
                testNames
                |> Array.map (fun n -> {| FullName = n; Data = () |})
                |> TestName.inferHierarchy
                |> Array.map (TestItem.fromNamedHierarchy testItemFactory tryGetLocation project.Project)

            return TestItem.fromProject testItemFactory project.Project project.Info.TargetFramework testHierarchy
        }


module Interactions =
    type ProjectRunRequest =
        {
            ProjectPath: ProjectPath
            /// examples: net6.0, net7.0, netcoreapp2.0, etc
            TargetFramework: TargetFramework
            ShouldDebug: bool
            Tests: TestItem array
            /// The Tests are listed due to a include filter, so when running the tests the --filter should be added
            HasIncludeFilter: bool
        }

    module TestRun =
        let normalizeLineEndings str =
            RegularExpressions.Regex.Replace(str, @"\r\n|\n\r|\n|\r", "\r\n")

        module Output =
            module private Ansi =
                let yellow (text: string) = $"\u001B[33m{text}\u001B[0m"
                let green (text: string) = $"\u001B[32m{text}\u001B[0m"
                let red (text: string) = $"\u001B[31m{text}\u001B[0m"

            module Symbols =
                let testPassed = Ansi.green "Passed"
                let testFailed = Ansi.red "Failed"
                let testSkipped = Ansi.yellow "Skipped"

            let appendLine (testRun: TestRun) (message: string) =
                // NOTE: New lines must be crlf https://code.visualstudio.com/api/extension-guides/testing#test-output
                testRun.appendOutput (sprintf "%s\r\n" (normalizeLineEndings message))

            let appendWarningLine (testRun: TestRun) (message: string) =
                appendLine testRun (message |> Ansi.yellow)

            let appendErrorLine (testRun: TestRun) (message: string) =
                appendLine testRun (message |> Ansi.red)

        let appendOutputLineForTest (testRun: TestRun) (testItem) (message: string) =
            let message = sprintf "%s\r\n" (normalizeLineEndings message)

            match TestItem.tryGetLocation testItem with
            | Some location -> testRun.appendOutput (message, location, testItem)
            | None -> testRun.appendOutput (message, test = testItem)

        let showEnqueued (testRun: TestRun) (testItems: TestItem array) =
            testItems |> Array.iter testRun.enqueued

        let showStarted (testRun: TestRun) (testItems: TestItem array) = testItems |> Array.iter testRun.started

        let showFailure (testRun: TestRun) (testItem: TestItem) (message: TestMessage) (duration: float) =
            testRun.failed (testItem, !^message, duration)

        let showError (testRun: TestRun) message (testItems: TestItem array) =
            let showSingle testItem =
                testRun.errored (testItem, !^ vscode.TestMessage.Create(!^message))

            testItems |> Array.iter showSingle

    type ProgressCancellable =
        | WithCancel
        | NoCancel

    let withProgress isCancellable f =
        let progressOpts = createEmpty<ProgressOptions>
        progressOpts.location <- U2.Case1 ProgressLocation.Window

        progressOpts.cancellable <-
            match isCancellable with
            | WithCancel -> Some true
            | NoCancel -> Some false

        window.withProgress (
            progressOpts,
            (fun progress cancellationToken -> f progress cancellationToken |> Promise.toThenable)
        )
        |> Promise.ofThenable

    let private filterEscapeCharacter = '\\'

    let private filterSpecialCharacters =
        [| '\\'; '('; ')'; '&'; '|'; '='; '!'; '~'; '"' |]

    let private filterSpecialCharactersSet = Set.ofArray filterSpecialCharacters

    let private buildFilterExpression (tests: TestItem array) =
        // Escape any special characters in test names, as per https://github.com/microsoft/vstest/blob/main/docs/filter.md
        let escapeFilterExpression (str: string) =

            if str.IndexOfAny(filterSpecialCharacters) < 0 then
                str
            else
                let builder = StringBuilder()

                for i = 0 to str.Length - 1 do
                    let currentChar = str[i]

                    if filterSpecialCharactersSet.Contains currentChar then
                        builder.Append(filterEscapeCharacter) |> ignore

                    builder.Append(currentChar) |> ignore

                builder.ToString()

        let testToFilterExpression (test: TestItem) =
            let isProbableParameterizedTest (test: TestItem) =
                match test.parent with
                | None -> false
                | Some parent ->
                    let parentPlusParentheses =
                        RegularExpressions.Regex($"{parent.label |> RegularExpressions.Regex.Escape}\s*\(")

                    parentPlusParentheses.IsMatch(test.label)

            let getFullNameOfParameterizedTest (test: TestItem) =
                // NOTE: For xUnit and MSTest, we're nesting the the parameterized test cases under their method name,
                //       but the cannonical fully qualified test name doesn't reflect this nesting, so we have to account for the parent
                //       There might be a better way to handle this. Perhaps dynamically adding a cannonical unique test id field to TestItem
                //       (like with TestFramework). Adding this to runnable TestItems would reduce edge cases and special behavior for running individual tests
                let maybeGrandParent = test.parent |> Option.bind (fun t -> t.parent)

                match maybeGrandParent with
                | None -> TestItem.getFullName test.id
                | Some grandParent ->
                    TestName.appendSegment
                        (TestItem.getFullName grandParent.id)
                        { Text = test.label
                          SeparatorBefore = string TestName.pathSeparator }

            let getFilterPath (test: TestItem) =
                if
                    (test.TestFramework = TestFrameworkId.XUnit
                     || test.TestFramework = TestFrameworkId.MsTest)
                    && isProbableParameterizedTest test
                then
                    getFullNameOfParameterizedTest test
                else
                    TestItem.getFullName test.id


            let fullTestName = getFilterPath test
            let escapedTestName = escapeFilterExpression fullTestName

            if escapedTestName.Contains(" ") && test.TestFramework = TestFrameworkId.NUnit then
                // workaround for https://github.com/nunit/nunit3-vs-adapter/issues/876
                // Potentially we are going to run multiple tests that match this filter
                let testPart = escapedTestName.Split(' ').[0]
                $"(FullyQualifiedName~{testPart})"
            // NOTE: using DisplayName allows single theory cases to be run for xUnit
            else if test.TestFramework = TestFrameworkId.XUnit then
                let operator = if test.children.size = 0 then "=" else "~"
                $"(DisplayName{operator}{escapedTestName})"
            // NOTE: MSTest can't filter to parameterized test cases
            //  Truncating before the case parameters will run all the theory cases
            //  example parameterized test name -> `MsTestTests.TestClass.theoryTest (2,3,5)`
            else if test.TestFramework = TestFrameworkId.MsTest && String.endWith ")" fullTestName then
                let truncateOnLast (separator: string) (toSplit: string) =
                    match toSplit.LastIndexOf(separator) with
                    | -1 -> toSplit
                    | index -> toSplit.Substring(0, index)

                let truncatedTestName = truncateOnLast @" \(" escapedTestName
                $"(FullyQualifiedName~{truncatedTestName})"
            else if test.children.size = 0 then
                $"(FullyQualifiedName={escapedTestName})"
            else
                $"(FullyQualifiedName~{escapedTestName})"

        let filterExpression =
            tests |> Array.map testToFilterExpression |> String.concat "|"

        filterExpression

    let private displayTestResultInExplorer (testRun: TestRun) (testItem: TestItem, testResult: TestResult) =

        match testResult.Outcome with
        | TestResultOutcome.NotExecuted -> testRun.skipped testItem
        | TestResultOutcome.Skipped -> testRun.skipped testItem
        | TestResultOutcome.Passed ->
            testResult.Output
            |> Option.iter (TestRun.appendOutputLineForTest testRun testItem)

            testRun.passed (testItem, testResult.Timing)

        | TestResultOutcome.Failed ->
            let fullErrorMessage =
                match testResult.ErrorMessage with
                | Some em ->
                    testResult.ErrorStackTrace
                    |> Option.map (fun stackTrace -> sprintf "%s\n%s" em stackTrace)
                    |> Option.defaultValue em
                | None -> "No error reported"

            let msg = vscode.TestMessage.Create(!^fullErrorMessage)
            msg.location <- TestItem.tryGetLocation testItem
            msg.expectedOutput <- testResult.Expected
            msg.actualOutput <- testResult.Actual

            TestRun.showFailure testRun testItem msg testResult.Timing

    let mergeTestResultsToExplorer
        (rootTestCollection: TestItemCollection)
        (testItemFactory: TestItem.TestItemFactory)
        (tryGetLocation: TestId -> LocationRecord option)
        (testRun: TestRun)
        (shouldDeleteMissing: bool)
        (expectedToRun: TestItem array)
        (testResults: TestResult array)
        =

        let tryRemove (testWithoutResult: TestItem) =
            let parentCollection =
                match testWithoutResult.parent with
                | Some parent -> parent.children
                | None -> rootTestCollection

            parentCollection.delete testWithoutResult.id


        let getOrMakeHierarchyPath (testResult: TestResult) =
            let testItemFactory (ti: TestItem.TestItemBuilder) =
                testItemFactory
                    { ti with
                        testFramework = testResult.TestFramework }

            TestItem.getOrMakeHierarchyPath
                rootTestCollection
                testItemFactory
                tryGetLocation
                testResult.ProjectFilePath
                testResult.TargetFramework
                testResult.FullTestName

        let treeItemComparable (t: TestItem) = TestItem.getId t

        let resultComparable (r: TestResult) =
            TestItem.constructId r.ProjectFilePath r.FullTestName

        let missing, expected, added =
            ArrayExt.venn treeItemComparable resultComparable expectedToRun testResults

        expected |> Array.iter (displayTestResultInExplorer testRun)

        if shouldDeleteMissing then
            missing |> Array.iter tryRemove

        added
        |> Array.iter (fun additionalResult ->
            let treeItem = getOrMakeHierarchyPath additionalResult

            displayTestResultInExplorer testRun (treeItem, additionalResult))

    let private trxResultToTestResult
        (projectFilePath: ProjectFilePath)
        (targetFramework: TargetFramework)
        (trxResult: TrxParser.TestWithResult)
        =

        let expected, actual =
            TestResult.tryExtractExpectedAndActual trxResult.UnitTestResult.Output.ErrorInfo.Message

        { FullTestName = trxResult.UnitTest.FullName
          Outcome = !!trxResult.UnitTestResult.Outcome
          Output = trxResult.UnitTestResult.Output.StdOut
          ErrorMessage = trxResult.UnitTestResult.Output.ErrorInfo.Message
          ErrorStackTrace = trxResult.UnitTestResult.Output.ErrorInfo.StackTrace
          Expected = expected
          Actual = actual
          Timing = trxResult.UnitTestResult.Duration.Milliseconds
          TestFramework = TestFrameworkId.tryFromExecutorUri trxResult.UnitTest.TestMethod.AdapterTypeName
          ProjectFilePath = projectFilePath
          TargetFramework = targetFramework }

    type TrimMissing = bool

    module TrimMissing =
        let Trim = true
        let NoTrim = false

    type MergeTestResultsToExplorer = TestRun -> TrimMissing -> TestItem array -> TestResult array -> unit

    let private runTestProject_withoutExceptionHandling
        (mergeResultsToExplorer: MergeTestResultsToExplorer)
        (makeTrxPath: string -> string)
        (testRun: TestRun)
        (cancellationToken: CancellationToken)
        (projectRunRequest: ProjectRunRequest)
        =
        promise {
            let projectPath = projectRunRequest.ProjectPath

            let runnableTests =
                projectRunRequest.Tests
                |> Array.collect TestItem.runnableChildren
                // NOTE: there can be duplicates if a child and parent are both selected in the explorer
                |> Array.distinctBy TestItem.getId


            TestRun.showStarted testRun runnableTests

            let filterExpression =
                if projectRunRequest.HasIncludeFilter then
                    Some(buildFilterExpression projectRunRequest.Tests)
                else
                    None

            let trxPath = makeTrxPath projectPath

            let! output =
                DotnetCli.test
                    projectPath
                    projectRunRequest.TargetFramework
                    (Some trxPath)
                    filterExpression
                    (projectRunRequest.ShouldDebug |> DotnetCli.DebugTests.ofBool)
                    cancellationToken

            TestRun.Output.appendLine testRun output

            let testResults =
                TrxParser.extractTrxResults trxPath
                |> Array.map (trxResultToTestResult projectPath projectRunRequest.TargetFramework)

            if Array.isEmpty testResults then
                let message =
                    $"WARNING: No tests ran for project \"{projectPath}\". \r\nThe test explorer might be out of sync. Try running a higher test or refreshing the test explorer"

                window.showWarningMessage (message) |> ignore
                TestRun.Output.appendWarningLine testRun message
            else
                mergeResultsToExplorer testRun TrimMissing.Trim runnableTests testResults
        }

    let runTestProject
        (mergeResultsToExplorer: MergeTestResultsToExplorer)
        (makeTrxPath: string -> string)
        (testRun: TestRun)
        (cancellationToken: CancellationToken)
        (projectRunRequest: ProjectRunRequest)
        =
        promise {
            try
                return!
                    runTestProject_withoutExceptionHandling
                        mergeResultsToExplorer
                        makeTrxPath
                        testRun
                        cancellationToken
                        projectRunRequest
            with e ->
                let message =
                    $"❌ Error running tests: \n    project: {projectRunRequest.ProjectPath} \n\n    error:\n        {e.Message}"

                TestRun.Output.appendErrorLine testRun message
                TestRun.showError testRun message projectRunRequest.Tests
        }

    module TestRunRequest =
        let isDebugRequested (runRequest: TestRunRequest) =
            runRequest.profile
            |> Option.map (fun p -> p.kind = TestRunProfileKind.Debug)
            |> Option.defaultValue false

    let private filtersToProjectRunRequests
        (rootTestCollection: TestItemCollection)
        (runRequest: TestRunRequest)
        : ProjectRunRequest array =
        let testSelection =
            runRequest.``include``
            |> Option.map Array.ofSeq
            |> Option.defaultValue (rootTestCollection.TestItems())

        testSelection
        |> Array.groupBy (TestItem.getId >> TestItem.getProjectPath)
        |> Array.map (fun (projectPath: string, tests) ->
            let project =
                Project.tryFindInWorkspace projectPath
                |> Option.bind (fun loadingState ->
                    match loadingState with
                    | Project.ProjectLoadingState.Loaded proj -> Some proj
                    | _ ->
                        let message = $"Could not run tests: project not loaded. {projectPath}"
                        invalidOp message)
                |> Option.defaultWith (fun () ->
                    let message = $"Could not run tests: project not found in workspace. {projectPath}"

                    logger.Error(message)
                    invalidOp message)

            let replaceProjectRootIfPresent (testItems: TestItem array) =
                let projectRootItemId = TestItem.constructProjectRootId project.Project

                testItems
                |> Array.collect (fun testItem ->
                    if testItem.id = projectRootItemId then
                        testItem.children.TestItems()
                    else
                        [| testItem |])
                |> Array.distinctBy TestItem.getId

            let shouldDebug = TestRunRequest.isDebugRequested runRequest

            let hasIncludeFilter =
                let isOnlyProjectSelected =
                    match tests with
                    | [| single |] -> single.id = (TestItem.constructProjectRootId project.Project)
                    | _ -> false

                (Option.isSome runRequest.``include``) && (not isOnlyProjectSelected)

            { ProjectPath = projectPath
              TargetFramework = project.Info.TargetFramework
              ShouldDebug = shouldDebug
              HasIncludeFilter = hasIncludeFilter
              Tests = replaceProjectRootIfPresent tests })

    let discoverTests_WithLanguageServer testItemFactory (rootTestCollection: TestItemCollection) tryGetLocation =
        withProgress NoCancel
        <| fun p progressCancelToken ->
            promise {
                let report message =
                    logger.Info message

                    p.report
                        {| message = Some message
                           increment = None |}

                let mergeTestItemCollections (target: TestItem array) (addition: TestItem array) : TestItem array =
                    let rec recurse (target: TestItem array) (addition: TestItem array) : TestItem array =
                        let targetOnly, conficted, addedOnly =
                            ArrayExt.venn TestItem.getId TestItem.getId target addition

                        let mergeSingle (targetItem: TestItem, addedItem: TestItem) =
                            let mergedChildren =
                                recurse (targetItem.children.TestItems()) (addedItem.children.TestItems())

                            addedItem.children.replace (ResizeArray mergedChildren)
                            addedItem

                        Array.concat [ targetOnly; addedOnly; conficted |> Array.map mergeSingle ]

                    recurse target addition

                let mutable discoveredTestsAccumulator: TestItem array =
                    rootTestCollection.TestItems()

                let mutable discoveredTestCount: int = 0

                let onTestDiscoveryProgress (discoveryUpdate: TestDiscoveryUpdate) : unit =
                    let writeTestLog (log: TestLogMessage) =
                        let message = $"[Discover Tests] {log.Message}"

                        match log.Level with
                        | TestLogLevel.Warning -> logger.Warn(message)
                        | TestLogLevel.Error -> logger.Error(message)
                        | TestLogLevel.Informational -> logger.Info(message)

                    try
                        discoveryUpdate.TestLogs |> Array.iter writeTestLog

                        let newItems =
                            discoveryUpdate.Tests |> TestItem.ofTestDTOs testItemFactory tryGetLocation

                        discoveredTestsAccumulator <- mergeTestItemCollections discoveredTestsAccumulator newItems
                        discoveredTestCount <- discoveredTestCount + (discoveryUpdate.Tests |> Array.length)

                        report $"Discovering tests: {discoveredTestCount} discovered"
                        rootTestCollection.replace (ResizeArray discoveredTestsAccumulator)
                    with e ->
                        logger.Debug("Incremental test discovery update threw an exception", e)

                report "Discovering tests"
                let! discoveryResponse = LanguageService.discoverTests onTestDiscoveryProgress ()

                let testItems =
                    discoveryResponse.Data
                    |> TestItem.ofTestDTOs testItemFactory tryGetLocation
                    |> ResizeArray

                rootTestCollection.replace (testItems)

                if testItems |> Seq.length = 0 then
                    window.showWarningMessage (
                        $"No tests discovered. Make sure your projects are restored, built, and can be run with dotnet test. Discovery logs can be found in Output > F# - Test Adapter "
                    )
                    |> ignore
            }

    let private runTests_WithLanguageServer
        mergeTestResultsToExplorer
        (rootTestCollection: TestItemCollection)
        (req: TestRunRequest)
        testRun
        =
        promise {
            try
                let expectedToRun =
                    req.``include``
                    |> Option.map Array.ofSeq
                    |> Option.defaultValue (rootTestCollection.TestItems())
                    |> Array.collect TestItem.runnableChildren

                let expectedTestsById = expectedToRun |> Array.map (fun t -> t.id, t) |> Map

                let mergeResults (shouldTrim: TrimMissing) (resultDtos: TestResultDTO array) =
                    let actuallyRan: TestResult array =
                        resultDtos |> Array.map TestResult.ofTestResultDTO

                    mergeTestResultsToExplorer testRun shouldTrim expectedToRun actuallyRan

                let showStarted (testItems: TestItemDTO array) =
                    try
                        let groups = testItems |> Array.groupBy (fun t -> t.ProjectFilePath)

                        groups
                        |> Array.iter (fun (projPath, activeTests) ->
                            let testIdsToStart =
                                activeTests |> Array.map (fun t -> TestItem.constructId projPath t.FullName)

                            let knownExplorerItems = testIdsToStart |> Array.choose expectedTestsById.TryFind
                            knownExplorerItems |> TestRun.showStarted testRun)
                    with ex ->
                        logger.Debug("Threw error while mapping active test items to the explorer", ex)

                let onTestRunProgress (progress: TestRunProgress) =
                    showStarted progress.ActiveTests
                    mergeResults TrimMissing.NoTrim progress.TestResults

                    let appendTestResultToOutput (testResult: TestResultDTO) =
                        match testResult.Outcome with
                        | TestOutcomeDTO.Passed ->
                            TestRun.Output.appendLine
                                testRun
                                $"{TestRun.Output.Symbols.testPassed} {testResult.TestItem.FullName}"
                        | TestOutcomeDTO.Failed ->
                            TestRun.Output.appendLine
                                testRun
                                $"{TestRun.Output.Symbols.testFailed} {testResult.TestItem.FullName}"
                        | TestOutcomeDTO.Skipped ->
                            TestRun.Output.appendLine
                                testRun
                                $"{TestRun.Output.Symbols.testSkipped} {testResult.TestItem.FullName}"
                        | TestOutcomeDTO.None ->
                            TestRun.Output.appendWarningLine testRun $"No outcome for {testResult.TestItem.FullName}"
                        | TestOutcomeDTO.NotFound ->
                            TestRun.Output.appendWarningLine testRun $"NotFound {testResult.TestItem.FullName}"
                        | _ ->
                            TestRun.Output.appendWarningLine
                                testRun
                                $"An unexpected test outcome was encountered for {testResult.TestItem.FullName}"

                    progress.TestResults |> Array.iter appendTestResultToOutput

                    let appendToTestRun testRun (log: TestLogMessage) =
                        match log.Level with
                        | TestLogLevel.Informational -> TestRun.Output.appendLine testRun log.Message
                        | TestLogLevel.Warning -> TestRun.Output.appendWarningLine testRun log.Message
                        | TestLogLevel.Error -> TestRun.Output.appendErrorLine testRun log.Message

                    progress.TestLogs |> Array.iter (appendToTestRun testRun)

                let onAttachDebugger (processId: int) =
                    VSCodeActions.launchDebugger (string processId)

                let filterExpression, projectSubset =
                    match req.``include`` with
                    | None -> None, None
                    | Some selectedCases when Seq.isEmpty selectedCases -> None, None
                    | Some selectedCases ->
                        let filter =
                            selectedCases
                            |> Array.ofSeq
                            |> Array.filter (fun t -> t.id |> TestItem.getFullName <> String.Empty)
                            |> buildFilterExpression
                            |> Some

                        let projectSubset =
                            selectedCases
                            |> Seq.map (TestItem.getId >> TestItem.getProjectPath)
                            |> Seq.distinct
                            |> Array.ofSeq
                            |> Some

                        filter, projectSubset

                logger.Debug($"Test Filter Expression: {filterExpression}")

                let shouldDebug = TestRunRequest.isDebugRequested req

                let! runResult =
                    LanguageService.runTests
                        onTestRunProgress
                        onAttachDebugger
                        projectSubset
                        filterExpression
                        shouldDebug

                mergeResults TrimMissing.Trim runResult.Data

                if Array.isEmpty runResult.Data then
                    let message =
                        $"WARNING: No tests ran. The test explorer might be out of sync. Try running a higher test group or refreshing the test explorer"

                    window.showWarningMessage (message) |> ignore
                    TestRun.Output.appendWarningLine testRun message
            with ex ->
                logger.Debug("Test run failed with exception", ex)
                TestRun.Output.appendErrorLine testRun $"The test run errored {Environment.NewLine}{string ex}"

                window.showErrorMessage (
                    "Test run errored. See TestResults or Output > F# - Test Adapter for more info"
                )
                |> ignore
        }

    let runHandler
        (testController: TestController)
        (tryGetLocation: TestId -> LocationRecord option)
        (makeTrxPath)
        (useLegacyDotnetCliIntegration: bool)
        (req: TestRunRequest)
        (_ct: CancellationToken)
        : U2<Thenable<unit>, unit> =

        let testRun = testController.createTestRun req

        logger.Debug("TestRunRequest", req)

        if testController.items.size < 1. then
            !! testRun.``end`` ()
        else

            let testItemFactory = TestItem.itemFactoryForController testController

            let mergeTestResultsToExplorer =
                mergeTestResultsToExplorer testController.items testItemFactory tryGetLocation

            let runTestProject =
                runTestProject mergeTestResultsToExplorer makeTrxPath testRun _ct

            let buildProject testRun projectRunRequest =
                promise {

                    let runnableTests = TestItem.runnableFromArray projectRunRequest.Tests

                    let projectPath = projectRunRequest.ProjectPath
                    let basePathArgs = Project.getOutputPaths ()

                    let! _ = DotnetCli.restore projectPath basePathArgs

                    let! buildStatus = MSBuild.invokeMSBuildWithCancel projectPath "Build" _ct basePathArgs

                    if buildStatus.Code <> Some 0 then
                        TestRun.showError testRun "Project build failed" runnableTests
                        TestRun.Output.appendErrorLine testRun $"❌ Failed to build project: {projectPath}"
                        return None
                    else
                        return Some projectRunRequest
                }

            promise {
                let projectRunRequests = filtersToProjectRunRequests testController.items req

                projectRunRequests
                |> Array.collect (fun rr -> rr.Tests |> TestItem.runnableFromArray)
                |> TestRun.showEnqueued testRun

                let! buildResults =
                    projectRunRequests
                    |> List.ofArray
                    |> Promise.mapExecuteForAll (buildProject testRun)

                let successfullyBuiltRequests = buildResults |> List.choose id

                if useLegacyDotnetCliIntegration then
                    let! _ =
                        successfullyBuiltRequests
                        |> (Promise.executeWithMaxParallel maxParallelTestProjects runTestProject)

                    testRun.``end`` ()
                else
                    do! runTests_WithLanguageServer mergeTestResultsToExplorer testController.items req testRun
                    testRun.``end`` ()
                    do! discoverTests_WithLanguageServer testItemFactory testController.items tryGetLocation

            }
            |> (Promise.toThenable >> (!^))

    let private discoverTests_WithDotnetCli
        testItemFactory
        tryGetLocation
        makeTrxPath
        report
        (rootTestCollection: TestItemCollection)
        cancellationToken
        builtTestProjects
        =
        promise {
            let warn (message: string) =
                logger.Warn(message)
                window.showWarningMessage (message) |> ignore

            let listDiscoveryProjects, trxDiscoveryProjects =
                builtTestProjects |> List.partition TestDiscovery.canListTestCasesWithCli

            let discoverTestsByListOnly project =
                report $"Discovering tests for {project.Project}"
                TestDiscovery.discoverTestsByCliListTests testItemFactory tryGetLocation cancellationToken project

            let! listDiscoveredPerProject =
                listDiscoveryProjects
                |> ListExt.mapKeepInputAsync discoverTestsByListOnly
                |> Promise.all

            trxDiscoveryProjects
            |> List.iter (ProjectPath.fromProject >> makeTrxPath >> Path.deleteIfExists)

            let! _ =
                trxDiscoveryProjects
                |> Promise.executeWithMaxParallel maxParallelTestProjects (fun project ->
                    let projectPath = project.Project
                    report $"Discovering tests for {projectPath}"
                    let trxPath = makeTrxPath projectPath |> Some

                    DotnetCli.test
                        projectPath
                        project.Info.TargetFramework
                        trxPath
                        None
                        DotnetCli.DebugTests.NoDebug
                        cancellationToken)

            let trxDiscoveredTests =
                TestDiscovery.discoverFromTrx testItemFactory tryGetLocation makeTrxPath trxDiscoveryProjects


            let listDiscoveredTests = listDiscoveredPerProject |> Array.map snd
            let newTests = Array.concat [ listDiscoveredTests; trxDiscoveredTests ]

            report $"Discovered {newTests |> Array.sumBy (TestItem.runnableChildren >> Array.length)} tests"
            rootTestCollection.replace (newTests |> ResizeArray)

            if builtTestProjects |> List.length > 0 && Array.length newTests = 0 then
                let message =
                    "Detected test projects but no tests. Make sure your tests can be run with `dotnet test`"

                window.showWarningMessage (message) |> ignore
                logger.Warn(message)

            else
                let possibleDiscoveryFailures =
                    Array.concat
                        [ let getProjectTests (ti: TestItem) = ti.children.TestItems()

                          listDiscoveredPerProject
                          |> Array.filter (snd >> getProjectTests >> Array.isEmpty)
                          |> Array.map (fst >> ProjectPath.fromProject)

                          trxDiscoveryProjects
                          |> Array.ofList
                          |> Array.map ProjectPath.fromProject
                          |> Array.filter (makeTrxPath >> Path.tryPath >> Option.isNone) ]

                if (not << Array.isEmpty) possibleDiscoveryFailures then
                    let projectList = String.Join("\n", possibleDiscoveryFailures)

                    warn
                        $"No tests discovered for the following projects. Make sure your tests can be run with `dotnet test` \n {projectList}"
        }

    let refreshTestList
        testItemFactory
        (rootTestCollection: TestItemCollection)
        tryGetLocation
        makeTrxPath
        useLegacyDotnetCliIntegration
        (cancellationToken: CancellationToken)
        =

        withProgress NoCancel
        <| fun p progressCancelToken ->
            promise {
                let report message =
                    logger.Info message

                    p.report
                        {| message = Some message
                           increment = None |}

                let cancellationToken =
                    CancellationToken.mergeTokens [ cancellationToken; progressCancelToken ]

                let testProjects = Project.getLoaded () |> List.filter ProjectExt.isTestProject

                logger.Debug(
                    "Refresh - Test Projects",
                    testProjects |> List.map ProjectPath.fromProject |> Array.ofList
                )

                let testProjectCount = List.length testProjects
                report $"Building {testProjectCount} test projects"


                let! buildOutcomePerProject =
                    testProjects
                    |> Promise.mapExecuteForAll (fun project ->
                        promise {
                            let projectPath = project.Project
                            logger.Info($"Building {projectPath}")

                            let basePathArgs = Project.getOutputPaths ()


                            let! processExit = MSBuild.invokeMSBuildWithCancel projectPath "Build" cancellationToken basePathArgs
                            return (project, processExit)
                        })



                let builtTestProjects, buildFailures =
                    buildOutcomePerProject
                    |> List.partition (fun (_, processExit) -> processExit.Code = Some 0)
                    |> ListExt.mapPartitioned fst

                if (not << List.isEmpty) buildFailures then
                    let message =
                        "Couldn't build test projects. Make sure you can build projects with `dotnet build`"

                    window.showErrorMessage (message) |> ignore
                    logger.Error(message, buildFailures |> List.map ProjectPath.fromProject)

                else if useLegacyDotnetCliIntegration then
                    do!
                        discoverTests_WithDotnetCli
                            testItemFactory
                            tryGetLocation
                            makeTrxPath
                            report
                            rootTestCollection
                            cancellationToken
                            builtTestProjects
                else
                    do! discoverTests_WithLanguageServer testItemFactory rootTestCollection tryGetLocation
            }

    let tryMatchTestBySuffix (locationCache: CodeLocationCache) (testId: TestId) =
        let matcher (testId: TestId) (locatedTestId: TestId) =
            testId.EndsWith(TestItem.getFullName locatedTestId)

        locationCache.GetKnownTestIds() |> Seq.tryFind (matcher testId)

    let tryGetLocation (locationCache: CodeLocationCache) testId =
        let cached = locationCache.GetById testId

        match cached with
        | Some _ -> cached
        | None ->
            tryMatchTestBySuffix locationCache testId
            |> Option.bind locationCache.GetById
            |> Option.tee (fun lr -> locationCache.Save(testId, lr))

    type CodeBasedTestId = TestId
    type ResultBasedTestId = TestId

    let onTestsDiscoveredInCode
        (testItemFactory: TestItem.TestItemFactory)
        (rootTestCollection: TestItemCollection)
        (locationCache: CodeLocationCache)
        (testsPerFileCache: Collections.Generic.Dictionary<string, TestItem array>)
        (displacedFragmentMapCache: Collections.Generic.Dictionary<ResultBasedTestId, CodeBasedTestId>)
        (testsForFile: TestForFile)
        =

        let onTestCodeMapped (filePath: string) (testsFromCode: TestItem array) =
            CodeLocationCache.cacheTestLocations locationCache filePath testsFromCode

            let tryMatchDisplacedTest (testId: ResultBasedTestId) : TestItem option =
                displacedFragmentMapCache.TryGet(testId)
                |> Option.orElseWith (fun () -> tryMatchTestBySuffix locationCache testId)
                |> Option.tee (fun matchedId -> displacedFragmentMapCache[testId] <- matchedId)
                |> Option.bind (fun matchedId -> TestItem.tryGetById matchedId testsFromCode)
                |> Option.tee (fun matchedTest ->
                    matchedTest
                    |> LocationRecord.testToLocation
                    |> Option.iter (fun lr -> locationCache.Save(testId, lr)))


            TestDiscovery.tryMatchCodeLocations testItemFactory tryMatchDisplacedTest rootTestCollection testsFromCode


            let cached = testsPerFileCache.TryGet(filePath)

            match cached with
            | None -> ()
            | Some previousTestsFromSameCode ->
                TestDiscovery.mergeCodeUpdates rootTestCollection previousTestsFromSameCode testsFromCode

            testsPerFileCache[filePath] <- testsFromCode

        TestItem.tryFromTestForFile testItemFactory testsForFile
        |> Option.iter (onTestCodeMapped testsForFile.file)


module Mailbox =
    let continuousLoop f (mailbox: MailboxProcessor<'t>) =
        let rec idleLoop () =
            async {
                let! message = mailbox.Receive()
                f message

                return! idleLoop ()
            }

        idleLoop ()

let activate (context: ExtensionContext) =

    let useLegacyDotnetCliIntegration =
        Configuration.get false "FSharp.TestExplorer.UseLegacyDotnetCliIntegration"

    let testController =
        tests.createTestController ("fsharp-test-controller", "F# Test Controller")

    let testItemFactory = TestItem.itemFactoryForController testController
    let locationCache = CodeLocationCache()
    let workspaceRoot = workspace.rootPath.Value

    let storageUri =
        context.storageUri
        |> Option.map (fun uri -> uri.fsPath)
        |> Option.defaultValue workspaceRoot

    logger.Debug("Extension Storage", storageUri)
    let makeTrxPath = TrxParser.makeTrxPath workspaceRoot storageUri

    let tryGetLocation = Interactions.tryGetLocation locationCache

    let runHandler =
        Interactions.runHandler testController tryGetLocation makeTrxPath useLegacyDotnetCliIntegration

    testController.createRunProfile ("Run F# Tests", TestRunProfileKind.Run, runHandler, true)
    |> unbox
    |> context.subscriptions.Add

    testController.createRunProfile ("Debug F# Tests", TestRunProfileKind.Debug, runHandler, false)
    |> unbox
    |> context.subscriptions.Add

    let testsPerFileCache = Collections.Generic.Dictionary<string, TestItem array>()
    // Multiple result items might point to the same code location, but there will never be more than one code-located test per result-based test item
    let displacedFragmentMapCache =
        Collections.Generic.Dictionary<ResultBasedTestId, CodeBasedTestId>()

    let onTestsDiscoveredInCode =
        Interactions.onTestsDiscoveredInCode
            testItemFactory
            testController.items
            locationCache
            testsPerFileCache
            displacedFragmentMapCache

    let codeTestsDiscoveredMailbox =
        MailboxProcessor<TestForFile>
            .Start(Mailbox.continuousLoop onTestsDiscoveredInCode)

    Notifications.testDetected.Invoke(fun testsForFile ->
        codeTestsDiscoveredMailbox.Post(testsForFile)
        None)
    |> unbox
    |> context.subscriptions.Add


    let refreshHandler cancellationToken =
        promise {
            try
                do!
                    Interactions.refreshTestList
                        testItemFactory
                        testController.items
                        tryGetLocation
                        makeTrxPath
                        useLegacyDotnetCliIntegration
                        cancellationToken
            with e ->
                logger.Error("Ionide test discovery threw an exception", e)
        }
        |> Promise.toThenable
        |> (!^)


    testController.refreshHandler <- Some refreshHandler

    let shouldAutoDiscoverTests =
        Configuration.get true "FSharp.TestExplorer.AutoDiscoverTestsOnLoad"

    let mutable hasInitiatedDiscovery = false

    Project.workspaceLoaded.Invoke(fun () ->
        if shouldAutoDiscoverTests && not hasInitiatedDiscovery then
            hasInitiatedDiscovery <- true

            if useLegacyDotnetCliIntegration then
                let trxTests =
                    TestDiscovery.discoverFromTrx testItemFactory tryGetLocation makeTrxPath

                let workspaceProjects = Project.getLoaded ()
                let initialTests = trxTests workspaceProjects
                initialTests |> Array.iter testController.items.add

                let cancellationTokenSource = vscode.CancellationTokenSource.Create()
                // NOTE: Trx results can be partial if the last test run was filtered, so also queue a refresh to make sure we discover all tests
                Interactions.refreshTestList
                    testItemFactory
                    testController.items
                    tryGetLocation
                    makeTrxPath
                    useLegacyDotnetCliIntegration
                    cancellationTokenSource.token
                |> Promise.start
            else
                Interactions.discoverTests_WithLanguageServer testItemFactory testController.items tryGetLocation
                |> Promise.start

        None)
    |> unbox
    |> context.subscriptions.Add

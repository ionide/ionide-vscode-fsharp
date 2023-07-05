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

type TestId = string
type ProjectPath = string
type TargetFramework = string

module ProjectPath =
    let inline ofString str = str

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

    member this.Type: string = this?``type``

[<RequireQualifiedAccess; StringEnum(CaseRules.None)>]
type TestResultOutcome =
    | NotExecuted
    | Failed
    | Passed

type TestResult =
    { FullTestName: string
      Outcome: TestResultOutcome
      ErrorMessage: string option
      ErrorStackTrace: string option
      Expected: string option
      Actual: string option
      Timing: float }

type TrxTestDef =
    { ExecutionId: string
      TestName: string
      ClassName: string }

    member self.FullName = TestName.fromPathAndTestName self.ClassName self.TestName


type TrxTestResult =
    { ExecutionId: string
      FullTestName: string
      Outcome: string
      ErrorMessage: string option
      ErrorStackTrace: string option
      Timing: TimeSpan }

module Path =

    let tryPath (path: string) =
        if node.fs.existsSync (U2.Case1 path) then
            Some path
        else
            None

    let getNameOnly (path: string) =
        node.path.basename (path, node.path.extname (path))

    let split (path: string) : string array =
        path.Split([| node.path.sep |], StringSplitOptions.RemoveEmptyEntries)

    let private join segments = node.path.join (segments)

    let removeSpecialRelativeSegments (path: string) : string =
        let specialSegments = set [ ".."; "." ]
        path |> split |> Array.skipWhile specialSegments.Contains |> join



module TrxParser =

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

    let extractTestDefinitionsFromSelector (xpathSelector: XPath.XPathSelector) : TrxTestDef array =
        let extractTestDef (index: int) _ : TrxTestDef =
            let index = index + 1

            let executionId =
                xpathSelector.SelectString $"/t:TestRun/t:TestDefinitions/t:UnitTest[{index}]/t:Execution/@id"

            let className =
                xpathSelector.SelectString $"/t:TestRun/t:TestDefinitions/t:UnitTest[{index}]/t:TestMethod/@className"

            let testName =
                xpathSelector.SelectString $"/t:TestRun/t:TestDefinitions/t:UnitTest[{index}]/t:TestMethod/@name"

            { ExecutionId = executionId
              TestName = testName
              ClassName = className }

        xpathSelector.Select<obj array> "/t:TestRun/t:TestDefinitions/t:UnitTest"
        |> Array.mapi extractTestDef

    let extractTestDefinitions (trxPath: string) =
        let selector = trxSelector trxPath
        extractTestDefinitionsFromSelector selector

    let extractTestResult (xpathSelector: XPath.XPathSelector) (executionId: string) : TrxTestResult =
        // NOTE: The test result's `testName` isn't always the full name. Some libraries handle it differently
        // Thus, it must be extracted from the test deff
        let className =
            xpathSelector.SelectString
                $"/t:TestRun/t:TestDefinitions/t:UnitTest[t:Execution/@id='{executionId}']/t:TestMethod/@className"

        let testName =
            xpathSelector.SelectString
                $"/t:TestRun/t:TestDefinitions/t:UnitTest[t:Execution/@id='{executionId}']/t:TestMethod/@name"

        let outcome =
            xpathSelector.SelectString $"/t:TestRun/t:Results/t:UnitTestResult[@executionId='{executionId}']/@outcome"

        let errorInfoMessage =
            xpathSelector.TrySelectString
                $"/t:TestRun/t:Results/t:UnitTestResult[@executionId='{executionId}']/t:Output/t:ErrorInfo/t:Message"

        let errorStackTrace =
            xpathSelector.TrySelectString
                $"/t:TestRun/t:Results/t:UnitTestResult[@executionId='{executionId}']/t:Output/t:ErrorInfo/t:StackTrace"

        let timing =
            let duration =
                xpathSelector.SelectString
                    $"/t:TestRun/t:Results/t:UnitTestResult[@executionId='{executionId}']/@duration"

            let success, ts = TimeSpan.TryParse(duration)

            if success then ts else TimeSpan.Zero

        { ExecutionId = executionId
          FullTestName = TestName.fromPathAndTestName className testName
          Outcome = outcome
          ErrorMessage = errorInfoMessage
          ErrorStackTrace = errorStackTrace
          Timing = timing }



    let extractTrxResults (trxPath: string) =
        let xpathSelector = trxSelector trxPath

        let trxDefToTrxResult (trxDef: TrxTestDef) =
            extractTestResult xpathSelector trxDef.ExecutionId

        extractTestDefinitionsFromSelector xpathSelector |> Array.map trxDefToTrxResult

    let inferHierarchy (testDefs: TrxTestDef array) : TestName.NameHierarchy<TrxTestDef> array =
        testDefs
        |> Array.map (fun td -> {| FullName = td.FullName; Data = td |})
        |> TestName.inferHierarchy




module DotnetCli =
    type StandardOutput = string
    type StandardError = string

    let restore
        (projectPath: string)
        : JS.Promise<Node.ChildProcess.ExecError option * StandardOutput * StandardError> =
        Process.exec "dotnet" (ResizeArray([| "restore"; projectPath |]))

    let private dotnetTest
        (projectPath: string)
        (targetFramework: string)
        (trxOutputPath: string option)
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

        Process.exec "dotnet" (ResizeArray(args))

    type TrxPath = string
    type ConsoleOutput = string

    let test
        (projectPath: string)
        (targetFramework: string)
        (trxOutputPath: string option)
        (filterExpression: string option)
        : JS.Promise<ConsoleOutput> =
        promise {
            // https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-test#filter-option-details

            let filter =
                match filterExpression with
                | None -> Array.empty
                | Some filterExpression -> [| "--filter"; filterExpression |]

            if filter.Length > 0 then
                logger.Debug("Filter", filter)

            let! _, stdOutput, stdError =
                dotnetTest projectPath targetFramework trxOutputPath [| "--no-build"; yield! filter |]

            logger.Debug("Test run exitCode", stdError)

            return (stdOutput + stdError)
        }

    let listTests projectPath targetFramework (shouldBuild: bool) =
        let splitLines (str: string) =
            str.Split([| "\n" |], StringSplitOptions.RemoveEmptyEntries)

        promise {
            let additionalArgs = if not shouldBuild then [| "--no-build" |] else Array.empty

            let! _, stdOutput, _ =
                dotnetTest projectPath targetFramework None [| "--list-tests"; yield! additionalArgs |]

            let testNames =
                stdOutput
                |> splitLines
                |> Array.map String.trim
                |> Array.skipWhile ((<>) "The following Tests are available:")
                |> Array.where (not << String.IsNullOrEmpty)
                |> Array.safeSkip 1

            return testNames
        }

type LocationRecord =
    { Uri: Uri; Range: Vscode.Range option }

module LocationRecord =
    let tryGetUri (l: LocationRecord option) = l |> Option.map (fun l -> l.Uri)
    let tryGetRange (l: LocationRecord option) = l |> Option.bind (fun l -> l.Range)

type CodeLocationCache() =
    let locationCache = Collections.Generic.Dictionary<TestId, LocationRecord>()

    member _.Save(testId: TestId, location: LocationRecord) = locationCache[testId] <- location

    member _.GetById(testId: TestId) = locationCache.TryGet testId

    member _.DeleteByFile(uri: Uri) =
        for kvp in locationCache do
            if kvp.Value.Uri.fsPath = uri.fsPath then
                locationCache.Remove(kvp.Key) |> ignore




module TestItem =

    let private idSeparator = " -- "

    let constructId (projectPath: ProjectPath) (fullName: FullTestName) : TestId =
        String.Join(idSeparator, [| projectPath; fullName |])

    let constructProjectRootId (projectPath: ProjectPath) : TestId = constructId projectPath ""

    let getFullName (testId: TestId) : FullTestName =
        let split =
            testId.Split(separator = [| idSeparator |], options = StringSplitOptions.None)

        FullTestName.ofString split.[1]

    let getProjectPath (testId: TestId) : ProjectPath =
        let split =
            testId.Split(separator = [| idSeparator |], options = StringSplitOptions.None)

        ProjectPath.ofString split.[0]

    let getId (t: TestItem) = t.id

    let runnableItems (root: TestItem) : TestItem array =
        // The goal is to collect here the actual runnable tests, they might be nested under a tree structure.
        let rec visit (testItem: TestItem) : TestItem array =
            if testItem.children.size = 0. then
                [| testItem |]
            else
                testItem.children.TestItems() |> Array.collect visit

        visit root

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
          children: TestItem array }

    type TestItemFactory = TestItemBuilder -> TestItem

    let itemFactoryForController (testController: TestController) =
        let factory builder =
            let testItem =
                match builder.uri with
                | Some uri -> testController.createTestItem (builder.id, builder.label, uri)
                | None -> testController.createTestItem (builder.id, builder.label)

            builder.children |> Array.iter testItem.children.add
            testItem.range <- builder.range

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
                  children = namedNode.Children |> Array.map recurse }

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
                      children = t.childs |> Array.map (fun n -> recurse fullName (Some t.moduleType) n) }

            ti?``type`` <- t.``type``
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
              children = children }

    let tryFromTestForFile (testItemFactory: TestItemFactory) (testsForFile: TestForFile) =
        let fileUri = vscode.Uri.parse (testsForFile.file, true)

        Project.tryFindLoadedProjectByFile fileUri.fsPath
        |> Option.map (fun project ->
            let projectPath = ProjectPath.ofString project.Project

            let fileTests =
                testsForFile.tests
                |> Array.map (fromTestAdapter testItemFactory fileUri projectPath)

            [| fromProject testItemFactory projectPath project.Info.TargetFramework fileTests |])

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
                          children = [||] }

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

        let testToLocation (testItem: TestItem) =
            match testItem.uri with
            | None -> None
            | Some uri -> Some { Uri = uri; Range = testItem.range }

        let saveTestItem (testItem: TestItem) =
            testToLocation testItem
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


module TestDiscovery =

    let mergeCodeLocations
        (testItemFactory: TestItem.TestItemFactory)
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
                      children = target.children.TestItems() }

            replacementItem?``type`` <- withUri?``type``
            (replacementItem, withUri)

        let rec recurse (target: TestItemCollection) (withUri: TestItem array) : unit =

            let treeOnly, matched, _codeOnly =
                ArrayExt.venn TestItem.getId TestItem.getId (target.TestItems()) withUri

            let updatePairs = matched |> Array.map cloneWithUri

            let newTestCollection = Array.concat [ treeOnly; updatePairs |> Array.map fst ]

            target.replace (ResizeArray newTestCollection)

            updatePairs
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
        (projectPaths: Project list)
        =

        let testProjects =
            projectPaths
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
                let heirarchy = TrxParser.inferHierarchy trxDefs

                let projectTests =
                    heirarchy
                    |> Array.map (TestItem.fromNamedHierarchy testItemFactory tryGetLocation projectPath)

                TestItem.fromProject testItemFactory projectPath project.Info.TargetFramework projectTests)


        treeItems

module Interactions =
    type ProjectRunRequest =
        {
            ProjectPath: ProjectPath
            /// examples: net6.0, net7.0, netcoreapp2.0, etc
            TargetFramework: TargetFramework
            Tests: TestItem array
            /// The Tests are listed due to a include filter, so when running the tests the --filter should be added
            HasIncludeFilter: bool
        }

    module TestRun =
        let normalizeLineEndings str =
            RegularExpressions.Regex.Replace(str, @"\r\n|\n\r|\n|\r", "\r\n")

        let appendOutputLine (testRun: TestRun) (message: string) =
            // NOTE: New lines must be crlf https://code.visualstudio.com/api/extension-guides/testing#test-output
            testRun.appendOutput (sprintf "%s\r\n" (normalizeLineEndings message))

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

    let withProgress f =
        let progressOpts = createEmpty<ProgressOptions>
        progressOpts.location <- U2.Case1 ProgressLocation.Window

        window.withProgress (
            progressOpts,
            (fun progress cancellationToken -> f progress cancellationToken |> Promise.toThenable)
        )
        |> Promise.ofThenable


    let private buildFilterExpression (tests: TestItem array) =
        let testToFilterExpression (test: TestItem) =
            let fullName = TestItem.getFullName test.id

            if test.children.size > 0 && fullName.Contains(" ") && test.Type = "NUnit" then
                // workaround for https://github.com/nunit/nunit3-vs-adapter/issues/876
                // Potentially we are going to run multiple tests that match this filter
                let testPart = fullName.Split(' ').[0]
                $"(FullyQualifiedName~{testPart})"
            else if test.children.size = 0 then
                $"(FullyQualifiedName={fullName})"
            else
                $"(FullyQualifiedName~{fullName})"

        let filterExpression =
            tests |> Array.map testToFilterExpression |> String.concat "|"

        filterExpression

    let private displayTestResultInExplorer (testRun: TestRun) (testItem: TestItem, testResult: TestResult) =

        match testResult.Outcome with
        | TestResultOutcome.NotExecuted -> testRun.skipped testItem
        | TestResultOutcome.Passed -> testRun.passed (testItem, testResult.Timing)
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
        (projectPath: ProjectPath)
        (targetFramework: TargetFramework)
        (expectedToRun: TestItem array)
        (testResults: TestResult array)
        =
        let tryRemove (testWithoutResult: TestItem) =
            let parentCollection =
                match testWithoutResult.parent with
                | Some parent -> parent.children
                | None -> rootTestCollection

            parentCollection.delete testWithoutResult.id


        let getOrMakeHierarchyPath =
            TestItem.getOrMakeHierarchyPath
                rootTestCollection
                testItemFactory
                tryGetLocation
                projectPath
                targetFramework

        let treeItemComparable (t: TestItem) = TestItem.getFullName t.id
        let resultComparable (r: TestResult) = r.FullTestName

        let missing, expected, added =
            ArrayExt.venn treeItemComparable resultComparable expectedToRun testResults

        expected |> Array.iter (displayTestResultInExplorer testRun)
        missing |> Array.iter tryRemove

        added
        |> Array.iter (fun additionalResult ->
            let treeItem = getOrMakeHierarchyPath additionalResult.FullTestName
            displayTestResultInExplorer testRun (treeItem, additionalResult))

    let private trxResultToTestResult (trxResult: TrxTestResult) =
        // Q: can I get these parameters down to just trxResult?
        let expected, actual =
            match trxResult.ErrorMessage with
            | None -> None, None
            | Some message ->
                let lines =
                    message.Split([| "\r\n"; "\n" |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (fun n -> n.TrimStart())

                let tryFind (startsWith: string) =
                    Array.tryFind (fun (line: string) -> line.StartsWith(startsWith)) lines
                    |> Option.map (fun line -> line.Replace(startsWith, "").TrimStart())

                tryFind "Expected:", tryFind "But was:"

        { FullTestName = trxResult.FullTestName
          Outcome = !!trxResult.Outcome
          ErrorMessage = trxResult.ErrorMessage
          ErrorStackTrace = trxResult.ErrorStackTrace
          Expected = expected
          Actual = actual
          Timing = trxResult.Timing.Milliseconds }

    type MergeTestResultsToExplorer =
        TestRun -> ProjectPath -> TargetFramework -> TestItem array -> TestResult array -> unit

    let runTestProject
        (mergeResultsToExplorer: MergeTestResultsToExplorer)
        (makeTrxPath: string -> string)
        (testRun: TestRun)
        (projectRunRequest: ProjectRunRequest)
        =
        promise {
            let projectPath = projectRunRequest.ProjectPath

            let runnableTests =
                projectRunRequest.Tests
                |> Array.collect TestItem.runnableItems
                // NOTE: there can be duplicates if a child and parent are both selected in the explorer
                |> Array.distinctBy TestItem.getId

            TestRun.showEnqueued testRun runnableTests

            let! _ = DotnetCli.restore projectPath
            let! buildStatus = MSBuild.invokeMSBuild projectPath "Build"

            if buildStatus.Code <> Some 0 then
                TestRun.showError testRun "Project build failed" runnableTests
                TestRun.appendOutputLine testRun $"❌ Failed to build project: {projectPath}"
            else
                TestRun.showStarted testRun runnableTests

                let filterExpression =
                    if projectRunRequest.HasIncludeFilter then
                        Some(buildFilterExpression projectRunRequest.Tests)
                    else
                        None

                let trxPath = makeTrxPath projectPath

                let! output =
                    DotnetCli.test projectPath projectRunRequest.TargetFramework (Some trxPath) filterExpression

                TestRun.appendOutputLine testRun output

                let testResults =
                    TrxParser.extractTrxResults trxPath |> Array.map trxResultToTestResult

                if Array.isEmpty testResults then
                    let message =
                        $"WARNING: No tests ran for project \"{projectPath}\". \r\nThe test explorer might be out of sync. Try running a higher test or refreshing the test explorer"

                    window.showWarningMessage (message) |> ignore
                    TestRun.appendOutputLine testRun message
                else
                    mergeResultsToExplorer
                        testRun
                        projectPath
                        projectRunRequest.TargetFramework
                        runnableTests
                        testResults
        }


    let private filtersToProjectRunRequests (rootTestCollection: TestItemCollection) (runRequest: TestRunRequest) =
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
                    let message =
                        $"Could not run tests: project does not found in workspace. {projectPath}"

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


            { ProjectPath = projectPath
              TargetFramework = project.Info.TargetFramework
              HasIncludeFilter = Option.isSome runRequest.``include``
              Tests = replaceProjectRootIfPresent tests })

    let runHandler
        (testController: TestController)
        (tryGetLocation: TestId -> LocationRecord option)
        (makeTrxPath)
        (req: TestRunRequest)
        (_ct: CancellationToken)
        : U2<Thenable<unit>, unit> =

        let testRun = testController.createTestRun req

        logger.Debug("TestRunRequest", req)

        if testController.items.size < 1. then
            !! testRun.``end`` ()
        else
            let projectRunRequests = filtersToProjectRunRequests testController.items req

            let testItemFactory = TestItem.itemFactoryForController testController

            let mergeTestResultsToExplorer =
                mergeTestResultsToExplorer testController.items testItemFactory tryGetLocation

            let runTestProject = runTestProject mergeTestResultsToExplorer makeTrxPath testRun

            promise {
                let! _ = projectRunRequests |> Array.map runTestProject |> Promise.all
                testRun.``end`` ()
            }
            |> (Promise.toThenable >> (!^))

    let refreshTestList testItemFactory (rootTestCollection: TestItemCollection) tryGetLocation makeTrxPath =
        withProgress
        <| fun p _ ->
            promise {
                let report message =
                    logger.Info message

                    p.report
                        {| message = Some message
                           increment = None |}

                let workspaceProjectPaths = ProjectExt.getAllWorkspaceProjects ()
                let totalRestoreCount = List.length workspaceProjectPaths

                let! _ =
                    workspaceProjectPaths
                    |> Promise.executeForAlli (fun i projPath ->
                        report $"Restoring projects ({i + 1} of {totalRestoreCount})"
                        DotnetCli.restore projPath)



                let testProjects = Project.getLoaded () |> List.filter ProjectExt.isTestProject

                let testProjectCount = List.length testProjects
                let testProjectPaths = testProjects |> List.map (fun p -> p.Project)
                logger.Debug("Refresh - Test Projects", testProjectPaths |> Array.ofList)



                report $"Building {testProjectCount} test projects"

                let! _ =
                    testProjectPaths
                    |> Promise.executeForAll (fun projectPath ->
                        logger.Info($"Building {projectPath}")
                        MSBuild.invokeMSBuild projectPath "Build")


                let librariesCapableOfListOnlyDiscovery = set [ "Expecto"; "xunit.abstractions" ]

                let listDiscoveryProjects, trxDiscoveryProjects =
                    testProjects
                    |> List.partition (fun project ->
                        project.PackageReferences
                        |> Array.exists (fun pr -> librariesCapableOfListOnlyDiscovery |> Set.contains pr.Name))

                let discoverTestsByListOnly (project: Project) =
                    promise {
                        report $"Discovering tests for {project.Project}"

                        let! testNames = DotnetCli.listTests project.Project project.Info.TargetFramework false

                        let testHierarchy =
                            testNames
                            |> Array.map (fun n -> {| FullName = n; Data = () |})
                            |> TestName.inferHierarchy
                            |> Array.map (TestItem.fromNamedHierarchy testItemFactory tryGetLocation project.Project)

                        return
                            TestItem.fromProject
                                testItemFactory
                                project.Project
                                project.Info.TargetFramework
                                testHierarchy
                    }

                let! listDiscoveredTests = listDiscoveryProjects |> List.map discoverTestsByListOnly |> Promise.all

                let! _ =
                    trxDiscoveryProjects
                    |> Promise.executeWithMaxParallel 2 (fun project ->
                        let projectPath = project.Project
                        report $"Discovering tests for {projectPath}"
                        let trxPath = makeTrxPath projectPath |> Some
                        DotnetCli.test projectPath project.Info.TargetFramework trxPath None)

                let trxDiscoveredTests =
                    TestDiscovery.discoverFromTrx testItemFactory tryGetLocation makeTrxPath trxDiscoveryProjects

                let newTests = Array.concat [ listDiscoveredTests; trxDiscoveredTests ]

                report $"Discovered {newTests |> Array.sumBy (TestItem.runnableItems >> Array.length)} tests"
                rootTestCollection.replace (newTests |> ResizeArray)

                if testProjectCount > 0 && Array.length newTests = 0 then
                    let message =
                        "Detected test projects but no tests. Make sure your tests can be run with `dotnet test`"

                    window.showWarningMessage (message) |> ignore
                    logger.Warn(message)

            }

    let onTestsDiscoveredInCode
        (testItemFactory: TestItem.TestItemFactory)
        (rootTestCollection: TestItemCollection)
        (locationCache: CodeLocationCache)
        (testsPerFileCache: Collections.Generic.Dictionary<string, TestItem array>)
        (testsForFile: TestForFile)
        =

        let onTestCodeMapped (filePath: string) (testsFromCode: TestItem array) =
            TestDiscovery.mergeCodeLocations testItemFactory rootTestCollection testsFromCode
            CodeLocationCache.cacheTestLocations locationCache filePath testsFromCode

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

    testController.createRunProfile (
        "Run F# Tests",
        TestRunProfileKind.Run,
        Interactions.runHandler testController locationCache.GetById makeTrxPath,
        true
    )
    |> unbox
    |> context.subscriptions.Add

    //    testController.createRunProfile ("Debug F# Tests", TestRunProfileKind.Debug, runHandler testController, true)
    //    |> unbox
    //    |> context.subscriptions.Add

    let testsPerFileCache = Collections.Generic.Dictionary<string, TestItem array>()

    let onTestsDiscoveredInCode =
        Interactions.onTestsDiscoveredInCode testItemFactory testController.items locationCache testsPerFileCache

    let codeTestsDiscoveredMailbox =
        MailboxProcessor<TestForFile>
            .Start(Mailbox.continuousLoop onTestsDiscoveredInCode)

    Notifications.testDetected.Invoke(fun testsForFile ->
        codeTestsDiscoveredMailbox.Post(testsForFile)
        None)
    |> unbox
    |> context.subscriptions.Add


    let refreshHandler cancellationToken =
        Interactions.refreshTestList testItemFactory testController.items locationCache.GetById makeTrxPath
        |> Promise.toThenable
        |> (!^)

    testController.refreshHandler <- Some refreshHandler

    let mutable hasInitiatedDiscovery = false

    Project.workspaceLoaded.Invoke(fun () ->
        if not hasInitiatedDiscovery then
            hasInitiatedDiscovery <- true

            let trxTests =
                TestDiscovery.discoverFromTrx testItemFactory locationCache.GetById makeTrxPath

            let workspaceProjects = Project.getLoaded ()
            let initialTests = trxTests workspaceProjects
            initialTests |> Array.iter testController.items.add

            // NOTE: Trx results can be partial if the last test run was filtered, so also queue a refresh to make sure we discover all tests
            Interactions.refreshTestList testItemFactory testController.items locationCache.GetById makeTrxPath
            |> Promise.start

        None)
    |> unbox
    |> context.subscriptions.Add

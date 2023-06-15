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
    ConsoleAndOutputChannelLogger(Some "TestExplorer", Level.DEBUG, None, Some Level.DEBUG)

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


module TrxParser =
    let guessTrxPath (projectPath: string) =
        node.path.resolve (node.path.dirname projectPath, "TestResults", "Ionide.trx")

    let tryGetTrxPath (projectPath: string) =
        let trxPath = guessTrxPath projectPath

        if node.fs.existsSync (U2.Case1 trxPath) then
            Some trxPath
        else
            None

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

    let projectHasTrx projectPath =
        let trxPath = guessTrxPath projectPath
        node.fs.existsSync (U2.Case1 trxPath)

    let extractProjectTestDefinitions (projectPath: string) =
        match tryGetTrxPath projectPath with
        | None -> Array.empty
        | Some trxPath ->
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

    let internal dotnetTest
        (projectPath: string)
        (additionalArgs: string array)
        : JS.Promise<Node.ChildProcess.ExecError option * StandardOutput * StandardError> =
        Process.exec
            "dotnet"
            (ResizeArray(
                [| "test"
                   projectPath
                   "--logger:\"trx;LogFileName=Ionide.trx\""
                   "--noLogo"
                   yield! additionalArgs |]
            ))

    type TrxPath = string
    type ConsoleOutput = string

    let runTests (projectPath: string) (filterExpression: string option) : JS.Promise<TrxPath * ConsoleOutput> =
        promise {
            // https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-test#filter-option-details

            let filter =
                match filterExpression with
                | None -> Array.empty
                | Some filterExpression -> [| "--filter"; filterExpression |]

            if filter.Length > 0 then
                logger.Debug("Filter", filter)

            let! _, stdOutput, stdError = dotnetTest projectPath [| "--no-build"; yield! filter |]

            logger.Debug("Test run exitCode", stdError)

            let trxPath = TrxParser.guessTrxPath projectPath
            return trxPath, (stdOutput + stdError)
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

    let tryFromTestForFile (testItemFactory: TestItemFactory) (testsForFile: TestForFile) =
        let fileUri = vscode.Uri.parse (testsForFile.file, true)

        Project.tryFindLoadedProjectByFile fileUri.fsPath
        |> Option.map (fun project ->
            let projectPath = ProjectPath.ofString project.Project

            testsForFile.tests
            |> Array.map (fromTestAdapter testItemFactory fileUri projectPath))

    let getOrMakeHierarchyPath
        (rootCollection: TestItemCollection)
        (itemFactory: TestItemFactory)
        (tryGetLocation: TestId -> LocationRecord option)
        (projectPath: ProjectPath)
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

        let pathSegments = TestName.splitSegments fullTestName
        recurse rootCollection "" pathSegments


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

    let discoverFromTrx testItemFactory (tryGetLocation: TestId -> LocationRecord option) () =
        let workspaceProjects = ProjectExt.getAllWorkspaceProjects ()

        let testProjects =
            workspaceProjects
            |> Array.ofList
            |> Array.filter (TrxParser.tryGetTrxPath >> Option.isSome)

        logger.Debug("Workspace Projects", testProjects)
        logger.Debug("Test Projects", testProjects)

        let trxTestsPerProject =
            testProjects
            |> Array.map (fun p -> (p, TrxParser.extractProjectTestDefinitions p))

        let treeItems =
            trxTestsPerProject
            |> Array.collect (fun (projPath, trxDefs) ->
                let projectUri = ProjectPath.ofString projPath
                let heirarchy = TrxParser.inferHierarchy trxDefs
                logger.Debug("Hierarchy", heirarchy)

                heirarchy
                |> Array.map (TestItem.fromNamedHierarchy testItemFactory tryGetLocation projectUri))

        logger.Debug("Tests", treeItems)

        treeItems

module Interactions =
    type ProjectRunRequest =
        {
            ProjectPath: ProjectPath
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
        progressOpts.location <- U2.Case1 ProgressLocation.Notification

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
            TestItem.getOrMakeHierarchyPath rootTestCollection testItemFactory tryGetLocation projectPath

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

    type MergeTestResultsToExplorer = TestRun -> ProjectPath -> TestItem array -> TestResult array -> unit

    let runTestProject
        (mergeResultsToExplorer: MergeTestResultsToExplorer)
        (testRun: TestRun)
        (projectRunRequest: ProjectRunRequest)
        =
        promise {
            let projectPath = projectRunRequest.ProjectPath
            let runnableTests = projectRunRequest.Tests |> Array.collect TestItem.runnableItems

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


                let! trxPath, output = DotnetCli.runTests projectPath filterExpression

                TestRun.appendOutputLine testRun output

                let testResults =
                    TrxParser.extractTrxResults trxPath |> Array.map trxResultToTestResult

                if Array.isEmpty testResults then
                    let message =
                        $"WARNING: No tests ran for project \"{projectPath}\". \r\nThe test explorer might be out of sync. Try running a higher test or refreshing the test explorer"

                    window.showWarningMessage (message) |> ignore
                    TestRun.appendOutputLine testRun message
                else
                    mergeResultsToExplorer testRun projectPath runnableTests testResults
        }


    let private filtersToProjectRunRequests (rootTestCollection: TestItemCollection) (runRequest: TestRunRequest) =
        let testSelection =
            runRequest.``include``
            |> Option.map Array.ofSeq
            |> Option.defaultValue (rootTestCollection.TestItems())

        testSelection
        |> Array.groupBy (fun t -> TestItem.getProjectPath t.id)
        |> Array.map (fun (projPath: string, tests) ->
            { ProjectPath = projPath
              HasIncludeFilter = Option.isSome runRequest.``include``
              Tests = tests })

    let runHandler
        (testController: TestController)
        (tryGetLocation: TestId -> LocationRecord option)
        (req: TestRunRequest)
        (_ct: CancellationToken)
        : U2<Thenable<unit>, unit> =

        let testRun = testController.createTestRun req

        logger.Debug("TestRunRequest", req)

        if testController.items.size < 1. then
            !! testRun.``end`` ()
        else
            let projectRunRequests = filtersToProjectRunRequests testController.items req

            logger.Debug("Project run requests", projectRunRequests)

            let testItemFactory = TestItem.itemFactoryForController testController

            let mergeTestResultsToExplorer =
                mergeTestResultsToExplorer testController.items testItemFactory tryGetLocation

            let runTestProject = runTestProject mergeTestResultsToExplorer testRun

            promise {
                let! _ = projectRunRequests |> Array.map runTestProject |> Promise.all
                testRun.``end`` ()
            }
            |> (Promise.toThenable >> (!^))


    let refreshTestList testItemFactory (rootTestCollection: TestItemCollection) tryGetLocation =
        promise {

            let! _ =
                ProjectExt.getAllWorkspaceProjects ()
                |> List.map DotnetCli.restore
                |> Promise.Parallel

            let testProjectPaths =
                Project.getInWorkspace ()
                |> List.choose (fun projectLoadState ->
                    match projectLoadState with
                    | Project.ProjectLoadingState.Loaded proj ->
                        let isTestProject =
                            proj.PackageReferences
                            |> Array.exists (fun pr ->
                                pr.Name = "Microsoft.TestPlatform.TestHost"
                                || pr.Name = "Microsoft.NET.Test.Sdk")

                        if isTestProject then Some proj.Project else None
                    | _ -> None)

            logger.Debug("Refresh - Test Projects", testProjectPaths |> Array.ofList)

            let! _ =
                testProjectPaths
                |> Promise.executeForAll (fun projectPath -> MSBuild.invokeMSBuild projectPath "Build")

            let! _ =
                testProjectPaths
                |> List.map (fun projectPath -> DotnetCli.dotnetTest projectPath [| "--no-build" |])
                |> Promise.Parallel

            let newTests =
                TestDiscovery.discoverFromTrx testItemFactory tryGetLocation () |> ResizeArray

            rootTestCollection.replace newTests
        }

    let onTestsDiscoveredInCode
        (testItemFactory: TestItem.TestItemFactory)
        (rootTestCollection: TestItemCollection)
        (locationCache: CodeLocationCache)
        (testsPerFileCache: Collections.Generic.Dictionary<string, TestItem array>)
        (testsForFile: TestForFile)
        =
        logger.Debug("TestsForFile", testsForFile)

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

    testController.createRunProfile (
        "Run F# Tests",
        TestRunProfileKind.Run,
        Interactions.runHandler testController locationCache.GetById,
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
        Interactions.refreshTestList testItemFactory testController.items locationCache.GetById
        |> Promise.toThenable
        |> (!^)

    testController.refreshHandler <- Some refreshHandler

    Project.workspaceLoaded.Invoke(fun () ->
        let discoverTests =
            TestDiscovery.discoverFromTrx testItemFactory locationCache.GetById

        let initialTests = discoverTests ()
        initialTests |> Array.iter testController.items.add

        // NOTE: Trx results can be partial if the last test run was filtered, so also queue a refresh to make sure we discover all tests
        Interactions.refreshTestList testItemFactory testController.items locationCache.GetById
        |> Promise.start

        None)
    |> unbox
    |> context.subscriptions.Add

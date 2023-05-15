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


module TestName =
    let pathSeparator = '.'

    let joinSegments (pathSegments: string list) =
        String.Join(string pathSeparator, pathSegments)

    let splitSegments (fullTestName: string) =
        fullTestName.Split(pathSeparator) |> List.ofSeq

    let fromPathAndLabel (parentPath: string) (label: string) =
        if parentPath = "" then label else $"{parentPath}.{label}"

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

    member self.FullName = TestName.fromPathAndLabel self.ClassName self.TestName


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
          FullTestName = TestName.fromPathAndLabel className testName
          Outcome = outcome
          ErrorMessage = errorInfoMessage
          ErrorStackTrace = errorStackTrace
          Timing = timing }

    type TrxTestDefHierarchy =
        { Name: string
          Path: string
          TestDef: TrxTestDef option
          Children: TrxTestDefHierarchy array }

        member self.FullName = TestName.fromPathAndLabel self.Path self.Name


    module TrxTestDefHierarchy =
        let mapFoldBack (f: TrxTestDefHierarchy -> 'a array -> 'a) (root: TrxTestDefHierarchy) : 'a =
            let rec recurse (trxDef: TrxTestDefHierarchy) =
                let mappedChildren = trxDef.Children |> Array.map recurse
                f trxDef mappedChildren

            recurse root

    type private TrxTestDefWithSplitPath =
        { tdef: TrxTestDef
          relativePath: string list }

    let inferHierarchy (testDefs: TrxTestDef array) : TrxTestDefHierarchy array =

        let withRelativePath tdef =
            { tdef = tdef
              relativePath = TestName.splitSegments tdef.ClassName }

        let popTopPath tdefWithPath =
            { tdefWithPath with
                relativePath = tdefWithPath.relativePath.Tail }

        let groupBy trxDef = trxDef.relativePath |> List.tryHead

        let rec recurse (traversed: string list) defsWithRelativePath : TrxTestDefHierarchy array =
            defsWithRelativePath
            |> Array.groupBy groupBy
            |> Array.collect (fun (group, tdefs) ->
                match group with
                | Some groupName ->
                    [| { Name = groupName
                         TestDef = None
                         Path = traversed |> List.rev |> TestName.joinSegments
                         Children = recurse (groupName :: traversed) (tdefs |> Array.map popTopPath) } |]
                | None ->
                    tdefs
                    |> Array.map (fun tdef ->
                        { Name = tdef.tdef.TestName
                          Path = tdef.tdef.ClassName
                          TestDef = Some tdef.tdef
                          Children = [||] }))

        testDefs |> Array.map withRelativePath |> recurse []




module DotnetTest =

    let internal dotnetTest (projectPath: string) (additionalArgs: string array) =
        Process.exec
            "dotnet"
            (ResizeArray(
                [| "test"
                   projectPath
                   // Project should already be built, perhaps we can point to the dll instead?
                   "--logger:\"trx;LogFileName=Ionide.trx\""
                   "--noLogo"
                   yield! additionalArgs |]
            ))

    let private runTestProject (projectPath: string) (filterExpression: string option) =
        promise {
            let filter =
                match filterExpression with
                | None -> Array.empty
                | Some filterExpression -> [| "--filter"; filterExpression |]

            if filter.Length > 0 then
                logger.Debug("Filter", filter)

            let! _, _, exitCode = dotnetTest projectPath [| "--no-build"; yield! filter |]

            logger.Debug("Test run exitCode", exitCode)

            let trxPath = TrxParser.guessTrxPath projectPath
            return trxPath
        }

    let runTests (projectPath: string) (filterExpression: string option) : JS.Promise<TestResult array> =
        let trxDefToTrxResult xpathSelector (trxDef: TrxTestDef) =
            TrxParser.extractTestResult xpathSelector trxDef.ExecutionId

        let trxResultToTestResult (trxResult: TrxTestResult) =
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

        logger.Debug("Nunit project", projectPath)

        promise {
            // https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-test#filter-option-details

            let! trxPath = runTestProject projectPath filterExpression

            logger.Debug("Trx file at", trxPath)

            let xpathSelector = TrxParser.trxSelector trxPath

            let testDefinitions = TrxParser.extractTestDefinitionsFromSelector xpathSelector

            let testResults =
                testDefinitions
                |> Array.map (trxDefToTrxResult xpathSelector >> trxResultToTestResult)

            logger.Debug("Project Test Results", testResults)


            return testResults
        }

type TestId = string

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

    let constructId (projectPath: string) (fullName: string) : TestId =
        String.Join(idSeparator, [| projectPath; fullName |])

    let getFullName (testId: TestId) =
        let split =
            testId.Split(separator = [| idSeparator |], options = StringSplitOptions.None)

        split.[1]

    let getProjectPath (testId: TestId) =
        let split =
            testId.Split(separator = [| idSeparator |], options = StringSplitOptions.None)

        split.[0]

    let getId (t: TestItem) = t.id

    let runnableItems (root: TestItem) : TestItem array =
        // The goal is to collect here the actual runnable tests, they might be nested under a tree structure.
        let rec visit (testItem: TestItem) : TestItem array =
            if testItem.children.size = 0. then
                [| testItem |]
            else
                testItem.children.TestItems() |> Array.collect visit

        visit root

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


    let fromTrxDef
        (itemFactory: TestItemFactory)
        (tryGetLocation: TestId -> LocationRecord option)
        projectPath
        (trxDef: TrxParser.TrxTestDefHierarchy)
        : TestItem =
        let rec recurse (trxDef: TrxParser.TrxTestDefHierarchy) =
            let id = constructId projectPath trxDef.FullName
            let location = tryGetLocation id

            itemFactory
                { id = id
                  label = trxDef.Name
                  uri = location |> LocationRecord.tryGetUri
                  range = location |> LocationRecord.tryGetRange
                  children = trxDef.Children |> Array.map recurse }

        recurse trxDef


    let fromTestAdapter
        (itemFactory: TestItemFactory)
        (uri: Uri)
        (projectPath: string)
        (t: TestAdapterEntry)
        : TestItem =
        let rec recurse (parentFullName: string) (t: TestAdapterEntry) =
            let fullName = TestName.fromPathAndLabel parentFullName t.name

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
                      children = t.childs |> Array.map (fun n -> recurse fullName n) }

            ti?``type`` <- t.``type``
            ti

        recurse "" t

    let tryFromTestForFile (testItemFactory: TestItemFactory) (testsForFile: TestForFile) =
        let fileUri = vscode.Uri.parse (testsForFile.file, true)

        Project.tryFindLoadedProjectByFile fileUri.fsPath
        |> Option.map (fun project ->
            testsForFile.tests
            |> Array.map (fromTestAdapter testItemFactory fileUri project.Project))

    let getOrMakeHierarchyPath
        (rootCollection: TestItemCollection)
        (itemFactory: TestItemFactory)
        (tryGetLocation: TestId -> LocationRecord option)
        (projectPath: string)
        (fullTestName: string)
        =
        let rec recurse (collection: TestItemCollection) (parentPath: string) (remainingPath: string list) =

            let currentLabel, remainingPath =
                match remainingPath with
                | currentLabel :: remainingPath -> (currentLabel, remainingPath)
                | [] -> "", []

            let fullName = TestName.fromPathAndLabel parentPath currentLabel
            let id = constructId projectPath fullName
            let maybeLocation = tryGetLocation id
            let existingItem = collection.get (id)

            let testItem =
                match existingItem with
                | Some existing -> existing
                | None ->
                    itemFactory
                        { id = id
                          label = currentLabel
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

    let discoverFromTrx testItemFactory (locationCache: CodeLocationCache) () =
        let allProjects = Project.getAll () |> Array.ofList

        let testProjects =
            allProjects |> Array.filter (TrxParser.tryGetTrxPath >> Option.isSome)

        logger.Debug("Projects", allProjects)
        logger.Debug("Test Projects", testProjects)

        let trxTestsPerProject =
            testProjects
            |> Array.map (fun p -> (p, TrxParser.extractProjectTestDefinitions p))

        let treeItems =
            trxTestsPerProject
            |> Array.collect (fun (projPath, trxDefs) ->
                let heirarchy = TrxParser.inferHierarchy trxDefs
                logger.Debug("Hierarchy", heirarchy)

                heirarchy
                |> Array.map (TestItem.fromTrxDef testItemFactory locationCache.GetById projPath))

        logger.Debug("Tests", treeItems)

        treeItems

module Interactions =
    type ProjectWithTests =
        {
            ProjectPath: string
            Tests: TestItem array
            /// The Tests are listed due to a include filter, so when running the tests the --filter should be added
            HasIncludeFilter: bool
        }

    module TestRun =
        let showEnqueued (testRun: TestRun) (testItems: TestItem array) =
            testItems |> Array.iter testRun.enqueued

        let showStarted (testRun: TestRun) (testItems: TestItem array) = testItems |> Array.iter testRun.started

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
        let filterValue =
            tests
            |> Array.map (fun t ->
                let fullName = TestItem.getFullName t.id

                if fullName.Contains(" ") && t.Type = "NUnit" then
                    // workaround for https://github.com/nunit/nunit3-vs-adapter/issues/876
                    // Potentially we are going to run multiple tests that match this filter
                    let testPart = fullName.Split(' ').[0]
                    $"(FullyQualifiedName~{testPart})"
                else
                    $"(FullyQualifiedName={fullName})")
            |> String.concat "|"

        filterValue

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

            match testItem.uri, testItem.range with
            | Some uri, Some range -> msg.location <- Some(vscode.Location.Create(uri, !^range))
            | _ -> ()

            msg.expectedOutput <- testResult.Expected
            msg.actualOutput <- testResult.Actual
            testRun.failed (testItem, !^msg, testResult.Timing)

    let mergeTestResultsToExplorer
        (rootTestCollection: TestItemCollection)
        (testItemFactory: TestItem.TestItemFactory)
        (tryGetLocation: TestId -> LocationRecord option)
        (testRun: TestRun)
        (projectPath: string)
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

    type MergeTestResultsToExplorer = TestRun -> string -> TestItem array -> TestResult array -> unit

    let runTestProject
        (mergeResultsToExplorer: MergeTestResultsToExplorer)
        (testRun: TestRun)
        (projectRunRequest: ProjectWithTests)
        =
        promise {
            let projectPath = projectRunRequest.ProjectPath
            let runnableTests = projectRunRequest.Tests

            TestRun.showEnqueued testRun runnableTests

            let! buildStatus = MSBuild.invokeMSBuild projectPath "Build"

            if buildStatus.Code <> Some 0 then
                TestRun.showError testRun "Project build failed" runnableTests
            else
                TestRun.showStarted testRun runnableTests

                let filterExpression =
                    if projectRunRequest.HasIncludeFilter then
                        Some(buildFilterExpression projectRunRequest.Tests)
                    else
                        None

                let! testResults = DotnetTest.runTests projectPath filterExpression

                if Array.isEmpty testResults then
                    let message =
                        $"No tests run for project \"{projectPath}\". \nThe test explorer might be out of sync. Try running a higher test or refreshing the test explorer"

                    window.showWarningMessage (message) |> ignore
                else
                    mergeResultsToExplorer testRun projectPath runnableTests testResults
        }



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
            let getRunnableTests (includeFilter: ResizeArray<TestItem> option) =
                let treeItemsToRun =
                    match includeFilter with
                    | Some includedTests -> includedTests |> Array.ofSeq
                    | None -> testController.TestItems()

                treeItemsToRun |> Array.collect TestItem.runnableItems

            let filtersToProjectRunRequests (runRequest: TestRunRequest) =
                getRunnableTests runRequest.``include``
                |> Array.groupBy (fun t -> TestItem.getProjectPath t.id)
                |> Array.map (fun (projPath: string, tests) ->
                    { ProjectPath = projPath
                      //IMPORTANT: don't actually filter until test discovery can handle partial result files
                      HasIncludeFilter = false
                      Tests = tests })

            let projectRunRequests = filtersToProjectRunRequests req

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


    let refreshTestList (testController: TestController) locationCache =
        let testItemFactory = TestItem.itemFactoryForController testController

        promise {
            let! _ =
                Project.getAll ()
                |> List.map (fun p -> DotnetTest.dotnetTest p [||])
                |> Promise.Parallel

            let newTests =
                TestDiscovery.discoverFromTrx testItemFactory locationCache () |> ResizeArray

            testController.items.replace newTests
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

    let discoverTests = TestDiscovery.discoverFromTrx testItemFactory locationCache
    let initialTests = discoverTests ()
    initialTests |> Array.iter testController.items.add

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
        Interactions.refreshTestList testController locationCache
        |> Promise.toThenable
        |> (!^)

    testController.refreshHandler <- Some refreshHandler

    if Array.isEmpty initialTests then
        Interactions.refreshTestList testController locationCache |> Promise.start

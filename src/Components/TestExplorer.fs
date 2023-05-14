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

    let intersectBy
        (leftIdf: 'Left -> 'Id)
        (rightIdf: 'Right -> 'Id)
        (left: 'Left array)
        (right: 'Right array)
        : ('Left * 'Right) array =
        let _, intersection, _ = venn leftIdf rightIdf left right
        intersection

    let mutalExclusion
        (leftIdf: 'Left -> 'Id)
        (rightIdf: 'Right -> 'Id)
        (left: 'Left array)
        (right: 'Right array)
        : ('Left array * 'Right array) =
        let (leftExclusive, _, rightExclusive) = venn leftIdf rightIdf left right
        (leftExclusive, rightExclusive)


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

module TestItemCollection =
    /// Replace an item in a TestItemCollection. Works only at depth 1 and will not consider nested test items.
    let replaceItem (parentCollection: TestItemCollection) (testItem: TestItem) =
        parentCollection.delete (testItem.id)
        parentCollection.add (testItem)

type TestController with

    member x.TestItems() : TestItem array = x.items.TestItems()

type TestItem with

    member this.Type: string = this?``type``

type TestItemAndProject =
    { TestItem: TestItem
      ProjectPath: string }

type TestWithFullName = { FullName: string; Test: TestItem }

type ProjectWithTests =
    {
        ProjectPath: string
        Tests: TestWithFullName array
        /// The Tests are listed due to a include filter, so when running the tests the --filter should be added
        HasIncludeFilter: bool
    }

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

type ProjectWithTestResults =
    { ProjectPath: string
      TestResults: TestResult array }

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

    let private buildFilterFromTests (tests: TestWithFullName array) =
        let filterValue =
            tests
            |> Array.map (fun t ->
                if t.FullName.Contains(" ") && t.Test.Type = "NUnit" then
                    // workaround for https://github.com/nunit/nunit3-vs-adapter/issues/876
                    // Potentially we are going to run multiple tests that match this filter
                    let testPart = t.FullName.Split(' ').[0]
                    $"(FullyQualifiedName~{testPart})"
                else
                    $"(FullyQualifiedName={t.FullName})")
            |> String.concat "|"

        [| "--filter"; filterValue |]

    let private runTestProject (projectWithTests: ProjectWithTests) =
        promise {
            let filter =
                if not projectWithTests.HasIncludeFilter then
                    Array.empty
                else
                    buildFilterFromTests projectWithTests.Tests

            if filter.Length > 0 then
                logger.Debug("Filter", filter)

            let! _, _, exitCode = dotnetTest projectWithTests.ProjectPath [| "--no-build"; yield! filter |]

            logger.Debug("Test run exitCode", exitCode)

            let trxPath = TrxParser.guessTrxPath projectWithTests.ProjectPath
            return trxPath
        }

    let runTests (projectWithTests: ProjectWithTests) : JS.Promise<TestResult array> =
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

        logger.Debug("Nunit project", projectWithTests)

        promise {
            // https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-test#filter-option-details

            let! trxPath = runTestProject projectWithTests

            logger.Debug("Trx file at", trxPath)

            let xpathSelector = TrxParser.trxSelector trxPath

            let testDefinitions = TrxParser.extractTestDefinitionsFromSelector xpathSelector

            let testResults =
                testDefinitions
                |> Array.map (trxDefToTrxResult xpathSelector >> trxResultToTestResult)

            logger.Debug("Project Test Results", testResults)


            return testResults
        }


module TestRun =
    let showEnqueued (testRun: TestRun) (tests: TestItem array) =
        tests |> Array.iter (fun (test) -> testRun.enqueued test)


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
        let copyUri (target: TestItem, withUri: TestItem) =
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

            let matches =
                ArrayExt.intersectBy TestItem.getId TestItem.getId (target.TestItems()) withUri

            let updatedItems = matches |> Array.map copyUri

            updatedItems
            |> Array.iter (fun (replacement, _) -> TestItemCollection.replaceItem target replacement)

            updatedItems
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
    /// Build test projects and return the succeeded and failed projects
    let buildProjects (projects: ProjectWithTests array) : Thenable<ProjectWithTests array * ProjectWithTests array> =
        let progressOpts = createEmpty<ProgressOptions>
        progressOpts.location <- U2.Case1 ProgressLocation.Notification

        window.withProgress (
            progressOpts,
            (fun progress _ctok ->
                projects
                |> Array.map (fun p ->
                    progress.report
                        {| message = Some $"Building {p.ProjectPath}"
                           increment = None |}

                    MSBuild.invokeMSBuild p.ProjectPath "Build" |> Promise.map (fun cpe -> p, cpe))
                |> Promise.all
                |> Promise.map (fun projects ->
                    let successfulBuilds =
                        projects
                        |> Array.choose (fun (project, { Code = code }) ->
                            match code with
                            | Some 0 -> Some project
                            | _ -> None)

                    let failedBuilds =
                        projects
                        |> Array.choose (fun (project, { Code = code }) ->
                            match code with
                            | Some 0 -> None
                            | _ -> Some project)

                    successfulBuilds, failedBuilds)
                |> Promise.toThenable)
        )

    let runTests (testRun: TestRun) (projects: ProjectWithTests array) : Thenable<ProjectWithTestResults array> =
        // Indicate in the UI that all the tests are running.
        Array.iter
            (fun (project: ProjectWithTests) ->
                Array.iter (fun (t: TestWithFullName) -> testRun.started t.Test) project.Tests)
            projects

        let progressOpts = createEmpty<ProgressOptions>
        progressOpts.location <- U2.Case1 ProgressLocation.Notification

        window.withProgress (
            progressOpts,
            (fun progress _ctok ->
                projects
                |> Array.map (fun project ->
                    progress.report
                        {| message = Some $"Running tests for {project.ProjectPath}"
                           increment = None |}

                    let pairWithProjectPath testResults : ProjectWithTestResults =
                        { ProjectPath = project.ProjectPath
                          TestResults = testResults }

                    DotnetTest.runTests project |> Promise.map pairWithProjectPath)
                |> Promise.all
                |> Promise.toThenable)
        )


    let runHandler
        (testController: TestController)
        (tryGetLocation: TestId -> LocationRecord option)
        (req: TestRunRequest)
        (_ct: CancellationToken)
        : U2<Thenable<unit>, unit> =

        let displayTestResultInExplorer (testRun: TestRun) (testItem: TestWithFullName, testResult: TestResult) =
            let testItem = testItem.Test

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

        let testRun = testController.createTestRun req

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
                |> Array.map (fun (t) ->
                    { FullName = TestItem.getFullName t.id
                      Test = t })
                |> Array.groupBy (fun twn -> TestItem.getProjectPath twn.Test.id)
                |> Array.map (fun (projPath: string, tests) ->
                    { ProjectPath = projPath
                      HasIncludeFilter = false //(not << Array.isEmpty) tests
                      Tests = tests })

            let projectsWithTestsToRun = filtersToProjectRunRequests req


            logger.Debug("Found projects", projectsWithTestsToRun)

            // let runTestsForProject (testRun: TestRun) (projectRunRequest : ProjectWithTests) =
            //     promise {

            //     }

            projectsWithTestsToRun
            |> Array.collect (fun pwt -> pwt.Tests |> Array.map (fun twn -> twn.Test))
            |> TestRun.showEnqueued testRun

            logger.Debug("Test run list in projects", projectsWithTestsToRun)

            promise {
                let! successfulProjects, failedProjects = buildProjects projectsWithTestsToRun

                // for projects that failed to build, mark their tests as failed
                failedProjects
                |> Array.iter (fun project ->
                    project.Tests
                    |> Array.iter (fun t ->
                        testRun.errored (t.Test, !^ vscode.TestMessage.Create(!^ "Project build failed"))))

                let! completedTestProjects = runTests testRun successfulProjects

                logger.Debug("Completed Test Projects", completedTestProjects)

                completedTestProjects
                |> Array.iter (fun (project: ProjectWithTestResults) ->
                    if Array.isEmpty project.TestResults then
                        let message =
                            $"No tests run for project \"{project.ProjectPath}\". \nThe test explorer might be out of sync. Try running a higher test or refreshing the test explorer"

                        window.showWarningMessage (message) |> ignore

                    let projectRunRequest =
                        projectsWithTestsToRun
                        |> Array.find (fun p -> p.ProjectPath = project.ProjectPath)

                    let expectedToRun = projectRunRequest.Tests
                    let treeItemComparable (t: TestWithFullName) = TestItem.getFullName t.Test.id
                    let resultComparable (r: TestResult) = r.FullTestName

                    let missing, expected, added =
                        ArrayExt.venn treeItemComparable resultComparable expectedToRun project.TestResults

                    let tryRemove (testWithoutResult: TestWithFullName) =
                        let parentCollection =
                            match testWithoutResult.Test.parent with
                            | Some parent -> parent.children
                            | None -> testController.items

                        parentCollection.delete testWithoutResult.Test.id


                    expected |> Array.iter (displayTestResultInExplorer testRun)
                    missing |> Array.iter tryRemove
                    let testItemFactory = TestItem.itemFactoryForController testController

                    let getOrMakeHierarchyPath =
                        TestItem.getOrMakeHierarchyPath
                            testController.items
                            testItemFactory
                            tryGetLocation
                            project.ProjectPath

                    added
                    |> Array.iter (fun additionalResult ->
                        let treeItem = getOrMakeHierarchyPath additionalResult.FullTestName

                        let testWithFullName =
                            { FullName = additionalResult.FullTestName
                              Test = treeItem }

                        displayTestResultInExplorer testRun (testWithFullName, additionalResult)))


                testRun.``end`` ()
            }
            |> unbox


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



let activate (context: ExtensionContext) =


    let testController =
        tests.createTestController ("fsharp-test-controller", "F# Test Controller")

    let testItemFactory = TestItem.itemFactoryForController testController
    let locationCache = CodeLocationCache()

    let testsPerFileCache =
        System.Collections.Generic.Dictionary<string, TestItem array>()

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

    Notifications.testDetected.Invoke(fun testsForFile ->

        logger.Debug("TestsForFile", testsForFile)

        let onTestCodeMapped (filePath: string) (testsFromCode: TestItem array) =
            TestDiscovery.mergeCodeLocations testItemFactory testController.items testsFromCode
            CodeLocationCache.cacheTestLocations locationCache filePath testsFromCode

            let cached = testsPerFileCache.TryGet(filePath)

            match cached with
            | None -> ()
            | Some previousTestsFromSameCode ->
                TestDiscovery.mergeCodeUpdates testController.items previousTestsFromSameCode testsFromCode

            testsPerFileCache[filePath] <- testsFromCode

        TestItem.tryFromTestForFile testItemFactory testsForFile
        |> Option.iter (onTestCodeMapped testsForFile.file)

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

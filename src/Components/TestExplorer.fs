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

type TestItemCollection with

    member x.TestItems() : TestItem array =
        let arr = ResizeArray<TestItem>()
        x.forEach (fun t _ -> !! arr.Add(t))
        arr.ToArray()

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
    { Test: TestItem
      FullTestName: string
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

    member self.FullName = $"{self.ClassName}.{self.TestName}"

type TrxTestResult =
    { ExecutionId: string
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

        member self.FullName = $"{self.Path}.{self.Name}"


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
        let pathSeparator = '.'

        let joinPath (pathSegments: string list) =
            String.Join(string pathSeparator, pathSegments)

        let withRelativePath tdef =
            { tdef = tdef
              relativePath = tdef.ClassName.Split(pathSeparator) |> List.ofSeq }

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
                         Path = traversed |> List.rev |> joinPath
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
          Outcome = outcome
          ErrorMessage = errorInfoMessage
          ErrorStackTrace = errorStackTrace
          Timing = timing }



module internal TestExplorerState =

    module ProjectMap =
        let mutable private _testToProjectMap =
            new System.Collections.Generic.Dictionary<string, string>()

        let mutable private _allProjects = Collections.Generic.HashSet<string>()
        let addTests (projectPath: string) (testIds: string array) : unit = _allProjects.Add(projectPath) |> ignore
        // Q: what info will I have for looking up. Just testItem id or could I use the trx test id?
        let allProjects () = _allProjects |> Array.ofSeq

        let ensureProject projectPath = _allProjects.Add(projectPath) |> ignore


module DotnetTest =

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

            let! _, _, exitCode =
                Process.exec
                    "dotnet"
                    (ResizeArray(
                        [| "test"
                           projectWithTests.ProjectPath
                           // Project should already be built, perhaps we can point to the dll instead?
                           "--no-restore"
                           "--logger:\"trx;LogFileName=Ionide.trx\""
                           "--noLogo"
                           yield! filter |]
                    ))

            logger.Debug("Test run exitCode", exitCode)

            let trxPath = TrxParser.guessTrxPath projectWithTests.ProjectPath
            return trxPath
        }

    let runProject (projectWithTests: ProjectWithTests) : JS.Promise<ProjectWithTestResults> =
        let trxDefToTrxResult xpathSelector (trxDef: TrxTestDef) =
            TrxParser.extractTestResult xpathSelector trxDef.ExecutionId

        let trxResultToTestResult (testWithName: TestWithFullName) (trxResult: TrxTestResult) =
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

            { Test = testWithName.Test
              FullTestName = testWithName.FullName
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

            // I don't think I need the fold anymore since the hierarchy is based on the trx in the first place
            // The tree should already account for testCases

            let matchTrxWithTreeItems (treeItems: TestWithFullName array) (trxDefs: TrxTestDef array) =

                let treeItemMap =
                    treeItems
                    |> Array.map (fun ti -> (ti.FullName, ti))
                    |> dict
                    |> Collections.Generic.Dictionary

                let trxDefMap =
                    trxDefs
                    |> Array.map (fun trxDef -> (trxDef.FullName, trxDef))
                    |> dict
                    |> Collections.Generic.Dictionary

                let treeItemKeys = set treeItemMap.Keys
                let trxDefKeys = set trxDefMap.Keys

                let idToTuple id = (treeItemMap.[id], trxDefMap.[id])

                let intersection = Set.intersect treeItemKeys trxDefKeys

                logger.Debug("Result Intersection")

                intersection |> Array.ofSeq |> Array.map idToTuple

            // logger.Debug("Test Definitions", testDefinitions)
            // logger.Debug("Test Definitions", testDefinitions)

            // let unmappedTests, mappedTests =
            //     projectWithTests.Tests
            //     |> Array.sortByDescending (fun t -> t.FullName)
            //     |> Array.fold
            //         (fun (trxDefs, mappedTests) (t) ->
            //             let linkedTests, remainingTests =
            //                 trxDefs
            //                 |> Array.partition (fun (trxDef: TrxTestDef) -> trxDef.FullName.StartsWith t.FullName)

            //             if Array.isEmpty linkedTests then
            //                 remainingTests, mappedTests
            //             else
            //                 remainingTests, ([| yield! mappedTests; (t, linkedTests) |]))
            //         (testDefinitions, [||])


            // PICKUP: No tests are showing as mapped, why? Or can I just get around it?
            let matchedTests = matchTrxWithTreeItems projectWithTests.Tests testDefinitions
            logger.Debug("Mapped Tests", matchedTests)


            let tests =
                matchedTests
                |> Array.map (fun (t, trxDef) -> trxDef |> trxDefToTrxResult xpathSelector |> trxResultToTestResult t)

            logger.Debug("Project Test Results", matchedTests)


            return
                { ProjectPath = projectWithTests.ProjectPath
                  TestResults = tests }
        }

let rec mapTest
    (tc: TestController)
    (uri: Uri)
    (moduleTypes: Collections.Generic.Dictionary<string, string>)
    (parentNameId: string)
    (t: TestAdapterEntry)
    : TestItem =
    let ti =
        tc.createTestItem (
            $"{uri.ToString()} -- {parentNameId} -- {Convert.ToBase64String(Encoding.UTF8.GetBytes(t.name))}",
            t.name,
            uri
        )

    ti.range <-
        Some(
            vscode.Range.Create(
                vscode.Position.Create(t.range.start.line, t.range.start.character),
                vscode.Position.Create(t.range.``end``.line, t.range.``end``.character)
            )
        )

    moduleTypes.Add(ti.id, t.moduleType)

    t.childs
    |> Array.iter (fun n -> mapTest tc uri moduleTypes $"{parentNameId}.{t.name}" n |> ti.children.add)

    ti?``type`` <- t.``type``
    ti



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

                DotnetTest.runProject project)
            |> Promise.all
            |> Promise.toThenable)
    )

module TestRun =
    let showEnqueued (testRun: TestRun) (tests: TestItem array) =
        tests |> Array.iter (fun (test) -> testRun.enqueued test)

module TestItem =

    let private idSeparator = " -- "

    let constructId (projectPath: string) (fullName: string) =
        String.Join(idSeparator, [| projectPath; fullName |])

    let getFullName (testId: string) =
        let split =
            testId.Split(separator = [| idSeparator |], options = StringSplitOptions.None)

        split.[1]

    let getProjectPath (testId: string) =
        let split =
            testId.Split(separator = [| idSeparator |], options = StringSplitOptions.None)

        split.[0]

    let runnableItems (root: TestItem) : TestItem array =
        // The goal is to collect here the actual runnable tests, they might be nested under a tree structure.
        let rec visit (testItem: TestItem) : TestItem array =
            if testItem.children.size = 0. then
                [| testItem |]
            else
                testItem.children.TestItems() |> Array.collect visit

        visit root

    let fromTrxDef
        (testController: TestController)
        projectPath
        (node: TrxParser.TrxTestDefHierarchy)
        children
        : TestItem =
        let ti =
            testController.createTestItem (constructId projectPath node.FullName, node.Name)

        children |> Array.iter ti.children.add

        // ti.range <-
        //     Some(
        //         vscode.Range.Create(
        //             vscode.Position.Create(t.range.start.line, t.range.start.character),
        //             vscode.Position.Create(t.range.``end``.line, t.range.``end``.character)
        //         )
        //     )

        // moduleTypes.Add(ti.id, t.moduleType)

        // t.childs
        // |> Array.iter (fun n -> mapTest testController uri moduleTypes $"{parentNameId}.{t.name}" n |> ti.children.add)

        // ti?``type`` <- t.``type``
        ti




let runHandler
    (tc: TestController)
    (moduleTypes: Collections.Generic.Dictionary<string, string>)
    (req: TestRunRequest)
    (_ct: CancellationToken)
    : U2<Thenable<unit>, unit> =

    let displayTestResultInExplorer (testRun: TestRun) (testResult: TestResult) =
        match testResult.Outcome with
        | TestResultOutcome.NotExecuted -> testRun.skipped testResult.Test
        | TestResultOutcome.Passed -> testRun.passed (testResult.Test, testResult.Timing)
        | TestResultOutcome.Failed ->
            let fullErrorMessage =
                match testResult.ErrorMessage with
                | Some em ->
                    testResult.ErrorStackTrace
                    |> Option.map (fun stackTrace -> sprintf "%s\n%s" em stackTrace)
                    |> Option.defaultValue em
                | None -> "No error reported"

            let ti = testResult.Test
            let msg = vscode.TestMessage.Create(!^fullErrorMessage)

            match ti.uri, ti.range with
            | Some uri, Some range -> msg.location <- Some(vscode.Location.Create(uri, !^range))
            | _ -> ()

            msg.expectedOutput <- testResult.Expected
            msg.actualOutput <- testResult.Actual
            testRun.failed (ti, !^msg, testResult.Timing)


    logger.Debug("Test run request", req)
    let tr = tc.createTestRun req

    if tc.items.size < 1. then
        !! tr.``end`` ()
    else

        let getTestsToRun (includeFilter: ResizeArray<TestItem> option) =
            let treeItemsToRun =
                match includeFilter with
                | Some includedTests -> includedTests |> Array.ofSeq
                | None -> tc.TestItems()

            treeItemsToRun
            |> Array.collect TestItem.runnableItems
            |> Array.map (fun (t) ->
                { FullName = TestItem.getFullName t.id
                  Test = t })


        let projectsWithTests =
            getTestsToRun req.``include``
            |> Array.groupBy (fun twn -> TestItem.getProjectPath twn.Test.id)
            |> Array.map (fun (projPath: string, tests) ->
                { ProjectPath = projPath
                  HasIncludeFilter = false
                  Tests = tests })

        logger.Debug("Found projects", projectsWithTests)

        //TODO: need to actually set test states
        projectsWithTests
        |> Array.collect (fun pwt -> pwt.Tests |> Array.map (fun twn -> twn.Test))
        |> TestRun.showEnqueued tr

        logger.Debug("Test run list in projects", projectsWithTests)

        promise {
            let! successfulProjects, failedProjects = buildProjects projectsWithTests

            // for projects that failed to build, mark their tests as failed
            failedProjects
            |> Array.iter (fun project ->
                project.Tests
                |> Array.iter (fun t -> tr.errored (t.Test, !^ vscode.TestMessage.Create(!^ "Project build failed"))))

            let! completedTestProjects = runTests tr successfulProjects

            logger.Debug("Completed Test Projects", completedTestProjects)

            completedTestProjects
            |> Array.iter (fun (project: ProjectWithTestResults) ->
                project.TestResults |> Array.iter (displayTestResultInExplorer tr))

            tr.``end`` ()
        }
        |> unbox


let activate (context: ExtensionContext) =


    let testController =
        tests.createTestController ("fsharp-test-controller", "F# Test Controller")

    let moduleTypes = Collections.Generic.Dictionary<string, string>()

    testController.createRunProfile (
        "Run F# Tests",
        TestRunProfileKind.Run,
        runHandler testController moduleTypes,
        true
    )
    |> unbox
    |> context.subscriptions.Add

    //    testController.createRunProfile ("Debug F# Tests", TestRunProfileKind.Debug, runHandler testController, true)
    //    |> unbox
    //    |> context.subscriptions.Add

    let allProjects = Project.getAll () |> Array.ofList

    let testProjects =
        allProjects |> Array.filter (TrxParser.tryGetTrxPath >> Option.isSome)

    logger.Debug("Projects", allProjects)
    logger.Debug("Test Projects", testProjects)

    let trxTestsPerProject =
        testProjects
        |> Array.map (fun p -> (p, TrxParser.extractProjectTestDefinitions p))

    let registerProjectTestsPairings ((projPath, tests): (string * TrxTestDef array)) =
        tests
        |> Array.map (fun t -> t.FullName)
        |> (TestExplorerState.ProjectMap.addTests projPath)

    trxTestsPerProject |> Array.iter registerProjectTestsPairings

    let treeItems =
        trxTestsPerProject
        |> Array.collect (fun (projPath, trxDefs) ->
            TrxParser.inferHierarchy trxDefs
            |> Array.map (TrxParser.TrxTestDefHierarchy.mapFoldBack (TestItem.fromTrxDef testController projPath)))

    logger.Debug("Tests", treeItems)

    treeItems |> Array.iter testController.items.add

// Notifications.testDetected.Invoke(fun res ->

//     logger.Debug("Res", res)

//     let res =
//         res.tests
//         |> Array.map (mapTest testController (vscode.Uri.parse (res.file, true)) moduleTypes "")

//     logger.Debug("Res Mapped", res)

//     // res
//     // |> Array.iter (fun testItem ->
//     //     let parentOpt =
//     //         testController.TestItems() |> Array.tryFind (fun i -> i.label = testItem.label)

//     //     match parentOpt with
//     //     | None -> testController.items.add testItem
//     //     | Some parent -> testItem.children.forEach (fun childTestItem _ -> parent.children.add childTestItem))

//     None)
// |> unbox
// |> context.subscriptions.Add

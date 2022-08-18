module Ionide.VSCode.FSharp.TestExploer

open System
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
    { TestItem: TestItem; Project: Project }

type TestWithFullName = { FullName: string; Test: TestItem }

type ProjectWithTests =
    {
        Project: Project
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
      Expected: string option
      Actual: string option
      Timing: float }

type ProjectWithTestResults =
    { Project: Project
      Tests: TestResult array }

module DotnetTest =
    let runProject (projectWithTests: ProjectWithTests) : JS.Promise<ProjectWithTestResults> =
        logger.Debug("Nunit project", projectWithTests)

        promise {
            // https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-test#filter-option-details
            let filter =
                if not projectWithTests.HasIncludeFilter then
                    Array.empty
                else
                    let filterValue =
                        projectWithTests.Tests
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

            if filter.Length > 0 then
                logger.Debug("Filter", filter)

            let! _, _, exitCode =
                Process.exec
                    "dotnet"
                    (ResizeArray(
                        [| "test"
                           projectWithTests.Project.Project
                           // Project should already be built, perhaps we can point to the dll instead?
                           "--no-restore"
                           "--logger:\"trx;LogFileName=Ionide.trx\""
                           "--noLogo"
                           yield! filter |]
                    ))

            logger.Debug("Test run exitCode", exitCode)

            let trxPath =
                node.path.resolve (node.path.dirname projectWithTests.Project.Project, "TestResults", "Ionide.trx")

            logger.Debug("Trx file at", trxPath)
            // probably possible to read via promise api
            let trxContent = node.fs.readFileSync (trxPath, "utf8")
            let xmlDoc = mkDoc trxContent

            let xpathSelector =
                XPath.XPathSelector(xmlDoc, "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")

            let testDefinitions =
                xpathSelector.Select<obj array> "/t:TestRun/t:TestDefinitions/t:UnitTest"
                |> Array.mapi (fun idx _ ->
                    let idx = idx + 1

                    let executionId =
                        xpathSelector.SelectString $"/t:TestRun/t:TestDefinitions/t:UnitTest[{idx}]/t:Execution/@id"

                    let className =
                        xpathSelector.SelectString
                            $"/t:TestRun/t:TestDefinitions/t:UnitTest[{idx}]/t:TestMethod/@className"

                    let test =
                        xpathSelector.SelectString $"/t:TestRun/t:TestDefinitions/t:UnitTest[{idx}]/t:TestMethod/@name"

                    $"{className}.{test}", executionId)
                |> Map.ofArray

            let tests =
                projectWithTests.Tests
                |> Array.map (fun t ->
                    // If the test contains a single quote, xpath expressions cannot escape this and it leads to a world of pain.
                    let executionId = Map.find t.FullName testDefinitions

                    let outcome =
                        xpathSelector.SelectString
                            $"/t:TestRun/t:Results/t:UnitTestResult[@executionId='{executionId}']/@outcome"

                    let errorInfoMessage =
                        xpathSelector.TrySelectString
                            $"/t:TestRun/t:Results/t:UnitTestResult[@executionId='{executionId}']/t:Output/t:ErrorInfo/t:Message"

                    let timing =
                        let duration =
                            xpathSelector.SelectString
                                $"/t:TestRun/t:Results/t:UnitTestResult[@executionId='{executionId}']/@duration"

                        TimeSpan.Parse(duration).TotalMilliseconds

                    let expected, actual =
                        match errorInfoMessage with
                        | None -> None, None
                        | Some message ->
                            let lines =
                                message.Split([| "\r\n"; "\n" |], StringSplitOptions.RemoveEmptyEntries)
                                |> Array.map (fun n -> n.TrimStart())

                            let tryFind (startsWith: string) =
                                Array.tryFind (fun (line: string) -> line.StartsWith(startsWith)) lines
                                |> Option.map (fun line -> line.Replace(startsWith, "").TrimStart())

                            tryFind "Expected:", tryFind "But was:"

                    { Test = t.Test
                      FullTestName = t.FullName
                      Outcome = !!outcome
                      ErrorMessage = errorInfoMessage
                      Expected = expected
                      Actual = actual
                      Timing = timing })

            return
                { Project = projectWithTests.Project
                  Tests = tests }
        }

let rec mapTest (tc: TestController) (uri: Uri) (t: TestAdapterEntry) : TestItem =
    let ti = tc.createTestItem (uri.ToString() + " -- " + string t.id, t.name, uri)

    ti.range <-
        Some(
            vscode.Range.Create(
                vscode.Position.Create(t.range.start.line, t.range.start.character),
                vscode.Position.Create(t.range.``end``.line, t.range.``end``.character)
            )
        )

    t.childs |> Array.iter (fun n -> mapTest tc uri n |> ti.children.add)

    ti?``type`` <- t.``type``
    ti

/// Get a flat list with all tests for each project
let getProjectsForTests (tc: TestController) (req: TestRunRequest) : ProjectWithTests array =
    let testsWithProject =
        let items =
            match req.``include`` with
            | None -> tc.TestItems()
            | Some includedTests -> includedTests.ToArray()

        items
        |> Array.choose (fun (t: TestItem) ->
            t.uri
            |> Option.bind (fun uri -> Project.tryFindLoadedProjectByFile uri.fsPath)
            |> Option.map (fun (project: Project) -> { TestItem = t; Project = project }))

    let rec getFullName (ti: TestItem) =
        match ti.parent with
        | Some p -> getFullName p + "." + ti.label
        | None -> ti.label

    let collectTests (testItem: TestItemAndProject) : TestWithFullName array =
        let rec visit (testItem: TestItem) : TestWithFullName array =
            if testItem.children.size = 0. then
                [| { Test = testItem
                     FullName = getFullName testItem } |]
            else
                testItem.children.TestItems() |> Array.collect visit

        // The goal is to collect here the actual runnable tests, they might be nested under a tree structure.
        visit testItem.TestItem

    testsWithProject
    |> Array.groupBy (fun entry -> entry.Project.Project)
    |> Array.map (fun (_projectName, tests) ->
        { Project = tests.[0].Project
          Tests = Array.collect collectTests tests
          HasIncludeFilter = Option.isSome req.``include`` })

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
                    {| message = Some $"Building {p.Project.Project}"
                       increment = None |}

                MSBuild.buildProjectPath "Build" p.Project |> Promise.map (fun cpe -> p, cpe))
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
                    {| message = Some $"Running tests for {project.Project.Project}"
                       increment = None |}

                DotnetTest.runProject project)
            |> Promise.all
            |> Promise.toThenable)
    )

let runHandler (tc: TestController) (req: TestRunRequest) (_ct: CancellationToken) : U2<Thenable<unit>, unit> =
    logger.Debug("Test run request", req)
    let tr = tc.createTestRun req

    if tc.items.size < 1. then
        !! tr.``end`` ()
    else
        let projectsWithTests = getProjectsForTests tc req
        logger.Debug("Found projects", projectsWithTests)

        projectsWithTests
        |> Array.iter (fun { Tests = tests } -> Array.iter (fun (test: TestWithFullName) -> tr.enqueued test.Test) tests)

        logger.Debug("Test run list in projects", projectsWithTests)

        promise {
            let! successfulProjects, failedProjects = buildProjects projectsWithTests

            failedProjects
            |> Array.iter (fun project ->
                project.Tests
                |> Array.iter (fun t -> tr.errored (t.Test, !^ vscode.TestMessage.Create(!^ "Project build failed"))))

            let! completedTestProjects = runTests tr successfulProjects

            completedTestProjects
            |> Array.iter (fun (project: ProjectWithTestResults) ->
                project.Tests
                |> Array.iter (fun (test: TestResult) ->
                    match test.Outcome with
                    | TestResultOutcome.NotExecuted -> tr.skipped test.Test
                    | TestResultOutcome.Passed -> tr.passed (test.Test, test.Timing)
                    | TestResultOutcome.Failed ->
                        test.ErrorMessage
                        |> Option.iter (fun em ->
                            let ti = test.Test
                            let msg = vscode.TestMessage.Create(!^em)
                            msg.location <- Some(vscode.Location.Create(ti.uri.Value, !^ti.range.Value))
                            msg.expectedOutput <- test.Expected
                            msg.actualOutput <- test.Actual
                            tr.failed (ti, !^msg, test.Timing))))

            tr.``end`` ()
        }
        |> unbox

let activate (context: ExtensionContext) =
    let testController =
        tests.createTestController ("fsharp-test-controller", "F# Test Controller")

    testController.createRunProfile ("Run F# Tests", TestRunProfileKind.Run, runHandler testController, true)
    |> unbox
    |> context.subscriptions.Add

    //    testController.createRunProfile ("Debug F# Tests", TestRunProfileKind.Debug, runHandler testController, true)
    //    |> unbox
    //    |> context.subscriptions.Add

    Notifications.testDetected.Invoke(fun res ->
        logger.Debug("Tests", res)

        let res =
            res.tests
            |> Array.map (mapTest testController (vscode.Uri.parse (res.file, true)))

        res
        |> Array.iter (fun testItem ->
            let parentOpt =
                testController.TestItems() |> Array.tryFind (fun i -> i.label = testItem.label)

            match parentOpt with
            | None -> testController.items.add testItem
            | Some parent -> testItem.children.forEach (fun childTestItem _ -> parent.children.add childTestItem))

        None)
    |> unbox
    |> context.subscriptions.Add

module Ionide.VSCode.FSharp.TestExploer

open System
open Fable.Core
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open Ionide.VSCode.FSharp.Import
open Ionide.VSCode.FSharp.Import.XmlDoc
open global.Node
open Fable.Core.JsInterop
open DTO
open Ionide.VSCode.Helpers
open System.Text.RegularExpressions

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
      Project: Project }

type TestWithFullName = { FullName: string; Test: TestItem }

type ProjectWithTests =
    { Project: Project
      Tests: TestWithFullName array
      /// The Tests are listed due to a include filter, so when running the tests the --filter should be added
      HasIncludeFilter: bool }

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

module Expecto =

    let private trySkip count seq =
        let mutable index = 0

        seq
        |> Seq.skipWhile (fun _ ->
            index <- index + 1
            index <= count)

    let private parseTestSummaryRecord (n: string) =
        let split = n.Split('[')
        // let loc = split.[1] |> String.replace "]" ""
        split.[0], ""

    let private getFailed () =
        try
            lastOutput
            |> Seq.collect (fun kv ->
                kv.Value.Split('\n')
                |> Seq.map String.trim
                |> Seq.skipWhile (not << String.startWith "Failed:")
                |> Seq.filter (not << String.startWith "Failed:")
                |> Seq.filter (not << String.startWith "Errored:")
                |> Seq.filter (not << String.IsNullOrWhiteSpace)
                |> Seq.map parseTestSummaryRecord)
            |> Seq.map (fun (n, _loc) -> n) //if n.Contains " " then sprintf "\"%s\"" n else n)
            |> Seq.toArray
        with
        | ex ->
            logger.Debug("Failed: " + ex.Message)
            [||]

    let private getPassed () =
        try
            lastOutput
            |> Seq.collect (fun kv ->
                kv.Value.Split('\n')
                |> Seq.map String.trim
                |> Seq.skipWhile (not << String.startWith "Passed:")
                |> Seq.takeWhile (not << String.startWith "Ignored:")
                |> trySkip 1
                |> Seq.map parseTestSummaryRecord)
            |> Seq.map (fun (n, _loc) -> n) //if n.Contains " " then sprintf "\"%s\"" n else n)
            |> Seq.toArray
        with
        | ex ->
            logger.Debug("Failed: " + ex.Message)
            [||]

    let private getIgnored () =
        try
            lastOutput
            |> Seq.collect (fun kv ->
                kv.Value.Split('\n')
                |> Seq.map String.trim
                |> Seq.skipWhile (not << String.startWith "Ignored:")
                |> Seq.takeWhile (not << String.startWith "Failed:")
                |> trySkip 1
                |> Seq.map parseTestSummaryRecord)
            |> Seq.map fst //if n.Contains " " then sprintf "\"%s\"" n else n)
            |> Seq.toArray
        with
        | ex ->
            logger.Debug("Failed: " + ex.Message)
            [||]

    let private getTimers () =
        try
            lastOutput
            |> Seq.collect (fun kv ->
                kv.Value.Split('\n')
                |> Seq.choose (fun l ->
                    let m = Regex.Match(l, """\[.*\] (.*) (passed|failed) in (.*)\.""")

                    if m.Success then
                        Some(m.Groups.[1].Value, m.Groups.[3].Value)
                    else
                        None))
            |> Seq.toArray

        with
        | ex ->
            logger.Debug("Failed: " + ex.Message)
            [||]

    let getErrors () =
        try
            lastOutput
            |> Seq.collect (fun kv ->

                let output = kv.Value.Split('\n')

                let errors =
                    output
                    |> Seq.mapi (fun id n -> (id, n))
                    |> Seq.choose (fun (id, l) ->
                        let m = Regex.Match(l, """\[.*\ ERR] (.*) failed""")

                        if m.Success then
                            Some(id, m.Groups.[1].Value)
                        else
                            None)
                    |> Seq.toArray

                errors
                |> Seq.map (fun (id, name) ->
                    let error =
                        output
                        |> Seq.skip (id + 1)
                        |> Seq.takeWhile (fun n ->
                            not (
                                String.IsNullOrWhiteSpace n
                                || n.StartsWith "["
                                || n.TrimStart().StartsWith "at Expecto."
                            ))
                        |> Seq.toArray

                    let expected =
                        error
                        |> Seq.tryFind (fun n -> n.Trim().StartsWith "expected:")
                        |> Option.map (fun t -> t.Split(':').[1])

                    let actual =
                        error
                        |> Seq.tryFind (fun n -> n.Trim().StartsWith "actual:")
                        |> Option.map (fun t -> t.Split(':').[1])

                    let error =
                        error
                        // |> Seq.take ((error |> Seq.length) - 1)
                        |> String.concat "\n"

                    name, error, expected, actual))
            |> Seq.toArray
        with
        | ex ->
            logger.Debug("Failed: " + ex.Message)
            [||]

    let promiseSleep (ms: float) =
        Promise.create (fun resolve _ -> setTimeout (fun () -> resolve ()) ms |> ignore)

    let runExpectoProject project args =
        match Project.getLauncher outputChannel project with
        | None -> Promise.lift ""
        | Some launcher ->
            promise {
                let exe = project.Output
                let! childProcess = launcher args

                return!
                    childProcess
                    |> Process.onOutput (fun out ->
                        lastOutput.[exe] <-
                            (lastOutput.TryGet exe |> Option.defaultValue "")
                            + out.toString ())
                    |> Process.toPromise
                    |> Promise.bind (fun n ->
                        promise {
                            let! _ = promiseSleep 1000.
                            return string (defaultArg n.Code 0)
                        })
            }

    let runProject (project: ProjectWithTests) : JS.Promise<ProjectWithTestResults> =
        outputChannel.clear ()
        lastOutput.Clear()
        failwith "todo"

//        projects
//        |> Seq.map (fun (proj) ->
//            runExpectoProject
//                proj
//                [ "--summary-location"
//                  "--debug"
//                  "--no-spinner"
//                  "--colours"
//                  "0" ])
//        |> Promise.all
//        |> Promise.map (fun _ ->
//            let timers = getTimers ()
//            let errors = getErrors ()
//
//            let tryGetTime (name: string) =
//                let x =
//                    timers
//                    |> Seq.tryPick (fun (n, t) ->
//                        if n.Trim('"', ' ', '\\', '/') = name.Trim('"', ' ', '\\', '/') then
//                            Some(t.TrimEnd('0'))
//                        else
//                            None)
//
//                defaultArg x ""
//
//            let tryGetError (name: string) =
//                let x =
//                    errors
//                    |> Seq.tryPick (fun (n, t, e, a) ->
//                        if n.Trim('"', ' ', '\\', '/') = name.Trim('"', ' ', '\\', '/') then
//                            Some(t, e, a)
//                        else
//                            None)
//
//                defaultArg x ("", None, None)
//
//            let failed =
//                getFailed ()
//                |> Array.map (fun name ->
//                    let (error, expected, actual) = tryGetError name
//
//                    {| FullName = name
//                       Timer = tryGetTime name
//                       ErrorMessage = error
//                       Expected = expected
//                       Actual = actual
//                       Runner = "Expecto" |})
//
//            let ignored =
//                getIgnored ()
//                |> Array.map (fun name ->
//                    {| FullName = name
//                       Runner = "Expecto" |})
//
//            let passed =
//                getPassed ()
//                |> Array.map (fun name ->
//                    {| FullName = name
//                       Timer = tryGetTime name
//                       Runner = "Expecto" |})
//
//            {| Failed = failed
//               Ignored = ignored
//               Passed = passed |}
//
//        )

module NUnit =
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
                path.resolve (path.dirname projectWithTests.Project.Project, "TestResults", "Ionide.trx")

            logger.Debug("Trx file at", trxPath)
            // probably possible to read via promise api
            let trxContent = fs.readFileSync (trxPath, "utf8")
            let xmlDoc = mkDoc trxContent

            let xpathSelector =
                XPath.XPathSelector(xmlDoc, "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")

            let tests =
                projectWithTests.Tests
                |> Array.map (fun t ->
                    let parts = t.FullName.Split('.')
                    let className = String.concat "." (Array.take (parts.Length - 1) parts)
                    let testName = parts.[parts.Length - 1]

                    let executionId =
                        xpathSelector.SelectString
                            $"/t:TestRun/t:TestDefinitions/t:UnitTest/t:TestMethod[@name='{testName}' and @className='{className}']/../t:Execution/@id"

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

    t.childs
    |> Array.iter (fun n -> mapTest tc uri n |> ti.children.add)

    ti?``type`` <- t.``type``
    ti

/// Get a flat list with all tests for each project
let getProjectsForTests (tc: TestController) (req: TestRunRequest) : ProjectWithTests array =
    logger.Debug("req included", req.include)
    logger.Debug("req excluded", req.exclude)

    let testsWithProject =
        let items =
            match req.include with
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
                testItem.children.TestItems()
                |> Array.collect visit

        // The goal is to collect here the actual runnable tests, they might be nested under a tree structure.
        visit testItem.TestItem

    testsWithProject
    |> Array.groupBy (fun entry -> entry.Project.Project)
    |> Array.map (fun (_projectName, tests) ->
        { Project = tests.[0].Project
          Tests = Array.collect collectTests tests
          HasIncludeFilter = Option.isSome req.include })

/// Build test projects and return the succeeded and failed projects
let buildProjects (projects: ProjectWithTests array) : JS.Promise<ProjectWithTests array * ProjectWithTests array> =
    projects
    |> Array.map (fun p ->
        MSBuild.buildProjectPath "Build" p.Project
        |> Promise.map (fun cpe -> p, cpe))
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

let runTests (testRun: TestRun) (projects: ProjectWithTests array) : JS.Promise<ProjectWithTestResults array> =
    // Indicate in the UI that all the tests are running.
    Array.iter
        (fun (project: ProjectWithTests) ->
            Array.iter (fun (t: TestWithFullName) -> testRun.started t.Test) project.Tests)
        projects

    projects
    |> Array.map (fun project ->
        let testKind =
            Array.head project.Tests
            |> fun test -> test.Test.Type

        match testKind with
        | "NUnit" -> NUnit.runProject project
        | "XUnit" -> failwith "todo"
        | "Expecto" -> Expecto.runProject project
        | unknown -> Promise.reject (exn $"Unexpected test type \"{unknown}\""))
    |> Promise.all

let runHandler (tc: TestController) (req: TestRunRequest) (_ct: CancellationToken) : U2<Thenable<unit>, unit> =
    logger.Debug("Test run request", req)
    let tr = tc.createTestRun req
    logger.Debug("Test run", tc.items.size < 1.)

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
            logger.Debug("Outputs", completedTestProjects)

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

    testController.createRunProfile ("Debug F# Tests", TestRunProfileKind.Debug, runHandler testController, true)
    |> unbox
    |> context.subscriptions.Add

    Notifications.testDetected.Invoke (fun res ->
        logger.Debug("Tests", res)

        let res =
            res.tests
            |> Array.map (mapTest testController (vscode.Uri.parse (res.file, true)))

        res |> Array.iter testController.items.add

        None)
    |> unbox
    |> context.subscriptions.Add

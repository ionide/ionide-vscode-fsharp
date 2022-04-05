module Ionide.VSCode.FSharp.TestExploer

open System
open System.Collections.Generic
open Fable.Core
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open global.Node
open Fable.Core.JsInterop
open DTO
open Ionide.VSCode.Helpers
open System.Text.RegularExpressions

let private lastOutput = Collections.Generic.Dictionary<string,string>()
let private outputChannel = window.createOutputChannel "F# - Test Adapter"
let private logger = ConsoleAndOutputChannelLogger(Some "TestExplorer", Level.DEBUG, None, Some Level.DEBUG)

module Expecto =

    let private trySkip count seq =
        let mutable index = 0
        seq
        |> Seq.skipWhile (fun _ ->
            index <- index + 1
            index <= count)

    let private parseTestSummaryRecord (n : string) =
        let split = n.Split ('[')
        // let loc = split.[1] |> String.replace "]" ""
        split.[0], ""

    let private getFailed () =
        try
            lastOutput
            |> Seq.collect (fun kv ->
                kv.Value.Split('\n')
                |> Seq.map(String.trim)
                |> Seq.skipWhile (not << String.startWith "Failed:")
                |> Seq.filter (not << String.startWith "Failed:")
                |> Seq.filter (not << String.startWith "Errored:")
                |> Seq.filter (not << String.IsNullOrWhiteSpace)
                |> Seq.map (parseTestSummaryRecord)
            )
            |> Seq.map(fun (n,_loc) -> n) //if n.Contains " " then sprintf "\"%s\"" n else n)
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
                |> Seq.map(String.trim)
                |> Seq.skipWhile (not << String.startWith "Passed:")
                |> Seq.takeWhile (not << String.startWith "Ignored:")
                |> trySkip 1
                |> Seq.map (parseTestSummaryRecord)
            )
            |> Seq.map(fun (n,_loc) -> n) //if n.Contains " " then sprintf "\"%s\"" n else n)
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
                |> Seq.map(String.trim)
                |> Seq.skipWhile (not << String.startWith "Ignored:")
                |> Seq.takeWhile (not << String.startWith "Failed:")
                |> trySkip 1
                |> Seq.map (parseTestSummaryRecord)
            )
            |> Seq.map(fun (n,_loc) -> n) //if n.Contains " " then sprintf "\"%s\"" n else n)
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
                        Some (m.Groups.[1].Value, m.Groups.[3].Value)
                    else
                        None
                    ))
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
                    |> Seq.mapi (fun id n -> (id,n))
                    |> Seq.choose (fun (id, l) ->
                        let m = Regex.Match(l, """\[.*\ ERR] (.*) failed""")
                        if m.Success then
                            Some (id, m.Groups.[1].Value)
                        else
                            None
                        )
                    |> Seq.toArray
                errors
                |> Seq.map (fun (id, name) ->
                    let error =
                        output
                        |> Seq.skip (id + 1)
                        |> Seq.takeWhile (fun n -> not (String.IsNullOrWhiteSpace n || n.StartsWith "[" || n.TrimStart().StartsWith "at Expecto.") )
                        |> Seq.toArray
                    let expected =
                        error
                        |> Seq.tryFind (fun n -> n.Trim().StartsWith "expected:")
                        |> Option.map(fun t -> t.Split(':').[1])

                    let actual =
                        error
                        |> Seq.tryFind (fun n -> n.Trim().StartsWith "actual:")
                        |> Option.map(fun t -> t.Split(':').[1])

                    let error =
                        error
                        // |> Seq.take ((error |> Seq.length) - 1)
                        |> String.concat "\n"

                    name, error, expected, actual
                )
            )
            |> Seq.toArray
        with
        | ex ->
            logger.Debug("Failed: " + ex.Message)
            [||]

    let promiseSleep (ms: float) =
        Promise.create(fun resolve _ ->
            setTimeout (fun () -> resolve ()) ms |> ignore)


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
                        lastOutput.[exe] <-  (lastOutput.TryGet exe |> Option.defaultValue "") + out.toString () )
                    |> Process.toPromise
                    |> Promise.bind (fun n ->
                        promise {
                            let! _  = promiseSleep 1000.
                            return string (defaultArg n.Code 0)
                        }
                    )
            }

    let runProjs projs =
        outputChannel.clear ()
        lastOutput.Clear()
        projs
        |> Seq.map (fun (proj) ->
            runExpectoProject proj ["--summary-location"; "--debug"; "--no-spinner"; "--colours"; "0"]
        )
        |> Promise.all
        |> Promise.map (fun _ ->
            let timers = getTimers ()
            let errors = getErrors ()
            let tryGetTime (name : string) =
                let x = timers |> Seq.tryPick (fun (n,t) -> if n.Trim( '"', ' ', '\\', '/') = name.Trim( '"', ' ', '\\', '/') then Some (t.TrimEnd('0')) else None )
                defaultArg x ""
            let tryGetError (name : string) =
                let x = errors |> Seq.tryPick (fun (n,t,e,a) -> if n.Trim( '"', ' ', '\\', '/') = name.Trim( '"', ' ', '\\', '/') then Some (t,e,a) else None )
                defaultArg x ("", None, None)


            let failed =
                getFailed ()
                |> Array.map (fun name ->
                    let (error, expected, actual) = tryGetError name
                    {|FullName = name;
                      Timer = tryGetTime name
                      ErrorMessage = error
                      Expected = expected
                      Actual = actual
                      Runner = "Expecto" |} )
            let ignored =
                getIgnored ()
                |> Array.map (fun name ->
                    {|FullName = name;
                      Runner = "Expecto" |})
            let passed =
                getPassed ()
                |> Array.map (fun name ->
                    {|FullName = name;
                      Timer = tryGetTime name
                      Runner = "Expecto" |} )
            {|Failed = failed; Ignored =  ignored; Passed = passed|}

        )

let rec mapTest (tc: TestController) (uri: Uri) (t: TestAdapterEntry): TestItem =
    let ti = tc.createTestItem(uri.ToString() + " -- " + string t.id, t.name, uri)
    ti.range <- Some t.range
    t.childs |> Array.iter (fun n -> mapTest tc uri n |> ti.children.add)
    ti?``type`` <- t.``type``
    ti

let rec flatTestList (tc: TestItemCollection): TestItem array =
    let ri = ResizeArray<TestItem>()
    tc.forEach(fun t tc -> ri.Add t |> unbox)
    tc.forEach(fun t tc -> flatTestList t.children |> Array.iter ri.Add |> unbox)
    [|
        yield! ri
    |]

let getProjectsForTets (tc: TestController): Project array =
    let allTests = flatTestList tc.items
    let uniqueUris =
        allTests
        |> Array.choose(fun t -> t.uri)
        |> Array.map (fun u -> u.fsPath)
        |> Array.distinct
    logger.Debug("Test run unique uris", uniqueUris)

    uniqueUris
    |> Array.choose(fun u -> Project.tryFindLoadedProjectByFile u)
    |> Array.distinctBy (fun t -> t.Project)

let buildProjects (projs: Project array): JS.Promise<string> =
    projs
    |> Array.fold (fun p proj ->  p |> Promise.bind (fun code ->
        if code = "1" then Promise.reject (exn "Build failed")
        else
            MSBuild.buildProjectPath "Build" proj
            |> Promise.map (string)

    )) (Promise.lift "")



let runHandler (tc: TestController) (req: TestRunRequest) (ct: CancellationToken) =
    logger.Debug("Test run request", req)
    let tr = tc.createTestRun(req)

    let allTests = flatTestList tc.items
    allTests |> Array.iter tr.enqueued
    logger.Debug("Test run list", allTests)

    let projs = getProjectsForTets tc
    logger.Debug("Projects", projs)

    let rec getFullName (ti: TestItem) =
        match ti.parent with
        | Some p -> getFullName p + "." + ti.label
        | None -> ti.label

    let testsByFullName =
        allTests
        |> Array.map (fun t -> {|FullName = getFullName t; Test = t|})

    let findByFullName (name: string) =
        testsByFullName
        |> Seq.tryFind (fun t -> t.FullName.Trim() = name.Trim())
        |> Option.map (fun t -> t.Test)

    promise {
        let! build = buildProjects projs
        allTests |> Array.iter tr.started
        if build.Contains "Code = 0"  then
            let! outputs = Expecto.runProjs projs
            logger.Debug("Outputs", outputs)


            outputs.Passed
            |> Array.choose (fun t -> findByFullName t.FullName)
            |> Array.iter (fun t -> tr.passed t)

            outputs.Ignored
            |> Array.choose (fun t -> findByFullName t.FullName)
            |> Array.iter (tr.skipped)

            outputs.Failed
            |> Array.choose (fun t -> findByFullName t.FullName |> Option.map (fun ti -> ti, t))
            |> Array.iter (fun (ti, t) ->
                let msg = vscode.TestMessage.Create(!^ t.ErrorMessage)
                msg.location <- Some (vscode.Location.Create(ti.uri.Value, !^ ti.range.Value))
                msg.expectedOutput <- t.Expected
                msg.actualOutput <- t.Actual
                tr.failed(ti, !^ msg))

        else
            allTests |> Array.iter(fun t -> tr.errored(t, !^ vscode.TestMessage.Create(!^ "Project build failed")))


        tr.``end``()
        return ()
    } |>  unbox

let activate (context: ExtensionContext) =
    let testController = tests.createTestController("fshar-test-controller", "F# Test Controller")

    testController.createRunProfile("Run F# Tests", TestRunProfileKind.Run, runHandler testController, true)
    |> unbox
    |> context.subscriptions.Add

    testController.createRunProfile("Debug F# Tests", TestRunProfileKind.Debug, runHandler testController, true)
    |> unbox
    |> context.subscriptions.Add

    Notifications.testDetected.Invoke (fun res ->
        logger.Debug("Tests", res)
        let res =
            res.tests
            |> Array.map (mapTest testController (vscode.Uri.parse(res.file, true)))

        res
        |> Array.iter( testController.items.add)

        None
    ) |> unbox |> context.subscriptions.Add

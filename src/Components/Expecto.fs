namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers

module Expecto =
    let private outputChannel = window.createOutputChannel "Expecto"
    let mutable private lastOutput = System.Collections.Generic.Dictionary<string,string>()
    let private logger = ConsoleAndOutputChannelLogger(Some "Expecto", Level.DEBUG, None, Some Level.DEBUG)
    let mutable private watcherEnabled = false
    let private statusBar = window.createStatusBarItem(2 |> unbox, 100.)


    let private getAutoshow () =
        let cfg = vscode.workspace.getConfiguration()
        cfg.get ("Expecto.autoshow", true)

    let private getSequenced () =
        let cfg = vscode.workspace.getConfiguration()
        cfg.get ("Expecto.runSequenced", false)

    let private getExpectoProjects () =
        Project.getLoaded ()
        |> Seq.where (fun p -> p.References |> List.exists (String.endWith "Expecto.dll" )  )
        |> Seq.toList

    let private getExpectoExes () =
        getExpectoProjects ()
        |> List.map (fun n -> n.Output)
        |> List.where (String.endWith ".exe")

    let getFilesToWatch () =
        let projects = Project.getLoaded () |> Seq.toArray
        let getProject (ref : string) = projects |> Array.tryFind (fun p -> p.Output.ToLower() = ref.ToLower())

        let rec loop p =
            [
                yield p.Project
                yield! p.Files
                yield! p.References |> List.collect (fun ref ->
                    match getProject ref with
                    | None -> [ref]
                    | Some proj -> loop proj)
            ]

        getExpectoProjects ()
        |> List.collect loop
        |> List.distinct
        |> List.toArray

    let private handleExpectoList (error : Error, stdout : Buffer, stderr : Buffer) =
        if(stdout.toString() = "") then
            [||]
        else
            stdout.toString().Split('\n')
            |> Array.filter((<>) "")
            |> Array.map (fun n -> n.Trim())


    let private getFailed () =
        lastOutput
        |> Seq.collect (fun kv ->
            kv.Value.Split('\n')
            |> Seq.skipWhile ((<>) "Failed:")
            |> Seq.takeWhile((<>) "Errored:")
            |> Seq.map(String.trim)
            |> Seq.skip 1
        )
        |> Seq.map(fun n -> if n.Contains " " then sprintf "\"%s\"" n else n)

    let private buildExpectoProjects watchMode =
        outputChannel.clear ()

        if getAutoshow () && not watchMode then outputChannel.show ()
        elif watchMode then statusBar.text <- "$(eye) Watch Mode - building"

        getExpectoProjects ()
        |> List.map(fun proj ->
            let path = proj.Project
            let msbuild = defaultArg Environment.msbuild "xbuild"
            logger.Debug ("%s %s", msbuild, path)

            Process.spawnWithNotification msbuild "" path outputChannel
            |> Process.toPromise)
        |> Promise.all
        |> Promise.bind (fun codes ->
            if codes |> Seq.exists ((<>) "0") then
                if not watchMode then
                    vscode.window.showErrorMessage("Build of Expecto tests failed", "Show")
                    |> Promise.map (fun n -> if n = "Show" then outputChannel.show () )
                    |> Promise.map (fun _ -> false)
                else
                    statusBar.text <- "$(eye) Watch Mode - building failed"
                    Promise.empty
            else
                Promise.lift true)

    let private runExpecto watchMode args =
        outputChannel.clear ()
        if getAutoshow () && not watchMode then outputChannel.show ()
        elif watchMode then statusBar.text <- "$(eye) Watch Mode - running"
        let args = if getSequenced () then args + " --sequenced" else args

        buildExpectoProjects watchMode
        |> Promise.bind (fun res ->
            if res then
                lastOutput.Clear()
                getExpectoExes ()
                |> List.map(fun exe ->
                    logger.Debug ("%s %s", exe, args)
                    lastOutput.[exe] <- ""
                    Process.spawnWithNotification exe "mono" args outputChannel
                    |> Process.onOutput (fun out -> lastOutput.[exe] <- lastOutput.[exe] + out.ToString () )
                    |> Process.toPromise)
                |> Promise.all
                |> Promise.bind (fun codes ->
                    if codes |> Seq.exists ((<>) "0") then
                        if not watchMode then
                            vscode.window.showErrorMessage("Expecto tests failed", "Show")
                            |> Promise.map (fun n -> if n = "Show" then outputChannel.show () )
                        else
                            let failed = getFailed () |> Seq.toArray

                            statusBar.text <- (sprintf "$(eye) Watch Mode - %d tests failed" failed.Length)
                            Promise.empty
                    else
                        if watchMode then
                            statusBar.text <- "$(eye) Watch Mode - tests passed"
                        Promise.empty)
            else Promise.empty)

    let private runAll watchMode = runExpecto watchMode "--summary"

    let private getTestCases () =
        buildExpectoProjects false
        |> Promise.bind (fun res ->
            if res then
                getExpectoExes ()
                |> List.map (fun exe ->
                    Process.exec exe "mono" "--list-tests"
                )
                |> Promise.all
                |> Promise.map (Seq.collect (handleExpectoList) >> ResizeArray)
            else
                Promise.lift (ResizeArray()))


    let private getTestLists () =
        let getTestList (s : string) =
            let all = s.Split ('/')
            match all with
            | [||] -> ""
            | [|x|] -> ""
            | xs -> xs.[0 .. all.Length - 2] |> String.concat "/"

        getTestCases ()
        |> Promise.map (Seq.map (getTestList) >> Seq.distinct >> Seq.filter ((<>) "") >> ResizeArray)

    let private runSingle () =
        window.showQuickPick (Case2 (getTestCases() ))
        |> Promise.bind(fun n ->
            if JS.isDefined n then
                (sprintf "--run \"%s\"" n) |> runExpecto false
            else
                Promise.empty
        )

    let private runList () =
        window.showQuickPick (Case2 (getTestLists() ))
        |> Promise.bind(fun n ->
            if JS.isDefined n then
                (sprintf "--filter \"%s\" --summary" n) |> runExpecto false
            else
                Promise.empty
        )

    let private runFailed () =
        getFailed ()
        |> String.concat " "
        |> sprintf "--run %s --summary"
        |> runExpecto false

    let private startWatchMode () =
        statusBar.text <- "$(eye) Watch Mode On"
        watcherEnabled <- true

    let private stopWatchMode () =
        statusBar.text <- "$(eye) Watch Mode Off"
        watcherEnabled <- false

    let private onFileChanged (uri : Uri) =
        let files = getFilesToWatch ()

        if watcherEnabled && files |> Array.exists (fun n -> uri.fsPath = n) then
            runAll true
        else
            Promise.empty

    let activate disposables =
        let registerCommand com (f: unit-> _) =
            vscode.commands.registerCommand(com, unbox<Func<obj,obj>> f)
            |> ignore

        registerCommand "Expecto.run" (fun _ -> runAll false)
        registerCommand "Expecto.runSingle" runSingle
        registerCommand "Expecto.runList" runList
        registerCommand "Expecto.runFailed" runFailed
        registerCommand "Expecto.startWatchMode" startWatchMode
        registerCommand "Expecto.stopWatchMode" stopWatchMode
        registerCommand "Expecto.watchMode" (fun _ -> outputChannel.show () )


        statusBar.text <- "$(eye) Watch Mode Off"
        statusBar.tooltip <- "Expecto continues testing"
        statusBar.command <- "Expecto.watchMode"
        statusBar.show ()
        let watcher = workspace.createFileSystemWatcher("**/*.*")
        let _ = watcher.onDidChange $ onFileChanged
        ()

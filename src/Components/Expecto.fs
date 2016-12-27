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

    let private getConfig () =
        let cfg = vscode.workspace.getConfiguration()
        cfg.get ("Expecto.autoshow", true)

    let private getExpectoProjects () =
        Project.getLoaded ()
        |> Seq.where (fun p -> p.References |> List.exists (String.endWith "Expecto.dll" )  )
        |> Seq.toList

    let private getExpectoExes () =
        getExpectoProjects ()
        |> List.map (fun n -> n.Output)


    let private buildExpectoProjects () =
        outputChannel.clear ()
        if getConfig () then outputChannel.show ()
        getExpectoProjects ()
        |> List.map(fun proj ->
            let path = proj.Project
            let msbuild = defaultArg Environment.msbuild "xbuild"
            Process.spawnWithNotification msbuild "" path outputChannel
            |> Process.toPromise)
        |> Promise.all
        |> Promise.bind (fun codes ->
            if codes |> Seq.exists ((<>) "0") then
                vscode.window.showErrorMessage("Build of Expecto tests failed", "Show")
                |> Promise.map (fun n -> if n = "Show" then outputChannel.show () )
                |> Promise.map (fun _ -> false)
            else
                Promise.lift true)

    let private runExpecto args =
        outputChannel.clear ()
        if getConfig () then outputChannel.show ()
        buildExpectoProjects ()
        |> Promise.bind (fun res ->
            if res then
                getExpectoExes ()
                |> List.map(fun exe ->
                    Process.spawnWithNotification exe "mono" args outputChannel
                    |> Process.toPromise)
                |> Promise.all
                |> Promise.bind (fun codes ->
                    if codes |> Seq.exists ((<>) "0") then
                        vscode.window.showErrorMessage("Expecto tests failed", "Show")
                        |> Promise.map (fun n -> if n = "Show" then outputChannel.show () )
                    else
                        Promise.empty)
            else Promise.empty)

    let private handleExpectoList (error : Error, stdout : Buffer, stderr : Buffer) =
        if(stdout.toString() = "") then
            [||]
        else
            stdout.toString().Split('\n')
            |> Array.filter((<>) "")
            |> Array.map (fun n -> n.Trim())

    let private getList () =
        buildExpectoProjects ()
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

    let private runAll () = runExpecto "--summary"

    let private runSingle () =
        window.showQuickPick (Case2 (getList() ))
        |> Promise.bind(fun n ->
            if JS.isDefined n then
                (sprintf "--run \"%s\"" n).Replace("\n", "") |> runExpecto
            else
                Promise.empty
        )

    let activate disposables =
        let registerCommand com (f: unit-> _) =
            vscode.commands.registerCommand(com, unbox<Func<obj,obj>> f)
            |> ignore

        registerCommand "Expecto.build" buildExpectoProjects
        registerCommand "Expecto.run" runAll
        registerCommand "Expecto.runSingle" runSingle
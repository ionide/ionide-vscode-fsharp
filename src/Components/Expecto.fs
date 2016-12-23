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
        |> List.iter(fun proj ->
            let path = proj.Project
            let msbuild = defaultArg Environment.msbuild "xbuild"
            Process.spawnWithNotification msbuild "" path outputChannel
            |> Process.onExit(fun (code) ->
                if code.ToString() <> "0" then
                    vscode.window.showErrorMessage("Build of Expecto tests failed", "Show")
                    |> Promise.map (fun n -> if n = "Show" then outputChannel.show () )
                    |> ignore)
            |> ignore
        )

    let private runExpecto () =
        outputChannel.clear ()
        if getConfig () then outputChannel.show ()
        getExpectoExes ()
        |> List.iter(fun exe ->
            Process.spawnWithNotification exe "mono" "--summary" outputChannel
            |> Process.onExit(fun (code) ->
                if code.ToString() <> "0" then
                    vscode.window.showErrorMessage("Expecto tests failed", "Show")
                    |> Promise.map (fun n -> if n = "Show" then outputChannel.show () )
                    |> ignore)
            |> ignore )

    let activate disposables =
        let registerCommand com (f: unit->unit) =
            vscode.commands.registerCommand(com, unbox<Func<obj,obj>> f)
            |> ignore

        registerCommand "Expecto.build" buildExpectoProjects
        registerCommand "Expecto.run" runExpecto
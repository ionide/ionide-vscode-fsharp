namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode
open Ionide.VSCode.Helpers



module WebPreview =
    let private previewUri = Uri.parse "webpreview://preview"
    let private eventEmitter = vscode.EventEmitter<Uri>()
    let private update = eventEmitter.event

    let mutable linuxPrefix = ""
    let mutable command = "packages/FAKE/tools/FAKE.exe"
    let mutable host = ""
    let mutable port = 8083
    let mutable script = ""
    let mutable build = ""
    let mutable startString = ""
    let mutable parameters = [||]
    let mutable startingPage = ""
    let mutable fakeProcess : child_process_types.ChildProcess Option = None

    let loadSettings () =
        linuxPrefix <- Settings.loadOrDefault (fun s -> s.WebPreview.linuxPrefix) "mono"
        command <- Settings.loadOrDefault (fun s -> s.WebPreview.command) "packages/FAKE/tools/FAKE.exe"
        host <- Settings.loadOrDefault (fun s -> s.WebPreview.host) "localhost"
        port <- Settings.loadOrDefault (fun s -> s.WebPreview.port) 8888
        script <- Settings.loadOrDefault (fun s -> s.WebPreview.script) "build.fsx"
        build <- Settings.loadOrDefault (fun s -> s.WebPreview.build) "Serve"
        startString <- Settings.loadOrDefault (fun s -> s.WebPreview.startString) "listener started"
        parameters <- Settings.loadOrDefault (fun s -> s.WebPreview.parameters) [||]
        startingPage <- Settings.loadOrDefault (fun s -> s.WebPreview.startingPage) ""
        ()

    let private createProvider () =

        let generate () =
            let src = sprintf "http://%s:%d/%s" host port startingPage
            let style = "height: 100%; width: 100%; background-color: white;"
            sprintf "<iframe style='%s' src='%s' />" style src
        let v = eventEmitter.event

        let p =
            { new TextDocumentContentProvider
            with
                member this.provideTextDocumentContent () = generate ()
            }
        p?onDidChange <- eventEmitter.event
        p


    let parseResponse o =
        if JS.isDefined o && isNotNull o then
            let str = o.ToString ()
            if str.Contains startString then
                vscode.commands.executeCommand("vscode.previewHtml", previewUri, 2)
                |> ignore
        ()

    let close () =
        try
            fakeProcess |> Option.iter (fun p ->
                p.kill ()
                fakeProcess <- None)
        with
        | _ -> ()

    let show () =
        loadSettings ()
        if fakeProcess.IsSome then close ()
        let cp =
            let args = sprintf "%s %s port=%d" script build port
            let args' = parameters |> Array.fold (fun acc e -> acc + " " + e) args
            Process.spawn command linuxPrefix args'

        cp.stdout?on $ ("readable", (fun n -> cp.stdout?read $ () |> parseResponse )) |> ignore
        cp.stderr?on $ ("readable", (cp.stdout?read $ () |> (fun o -> if JS.isDefined o && isNotNull o then Browser.console.error(o.ToString())) )) |> ignore
        fakeProcess <- Some cp



    let activate (disposables : Disposable[]) =
        let prov = createProvider ()
        workspace.registerTextDocumentContentProvider("webpreview" |> unbox, prov) |> ignore

        commands.registerCommand("webpreview.Show", show |> unbox<Func<obj,obj>>) |> ignore
        commands.registerCommand("webpreview.Refresh", (fun _ -> eventEmitter.fire previewUri) |> unbox<Func<obj,obj>>) |> ignore
        ()
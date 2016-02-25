namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.languages
open FunScript.TypeScript.child_process
open FunScript.TypeScript.fs

open DTO
open Ionide.VSCode
open Ionide.VSCode.Helpers


[<ReflectedDefinition>]
module WebPreview =
    [<FunScript.JSEmitInline "(vscode.workspace.registerTextDocumentContentProvider({0}, {1}))">]
    let registerTextDocumentContentProvider(scheme : string, provider : TextDocumentContentProvider) : Disposable = failwith "JS"

    let private previewUri = Uri.parse "webpreview://preview"
    let private eventEmitter = EventEmitter.Create ()
    let private update = eventEmitter.fire


    let mutable linuxPrefix = ""
    let mutable command = "packages/FAKE/tools/FAKE.exe"
    let mutable host = ""
    let mutable port = 8083
    let mutable script = ""
    let mutable build = ""
    let mutable startString = ""
    let mutable parameters = [||]
    let mutable startingPage = ""
    let mutable fakeProcess : ChildProcess Option = None




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
        let provider = createEmpty<TextDocumentContentProvider> ()

        let generate () =
            let src = sprintf "http://%s:%d/%s" host port startingPage
            let style = "height: 100%; width: 100%; background-color: white;"
            sprintf "<iframe style='%s' src='%s' />" style src

        provider.``provideTextDocumentContent <-``((fun _ -> generate ()) |> unbox<_>)
        provider.onDidChange <- eventEmitter.event
        provider

    let parseResponse o =
        if JS.isDefined o && o <> null then
            let str =  o.ToString ()
            if str.Contains startString then
                vscode.commands.Globals.executeCommandOverload2("vscode.previewHtml", previewUri, 2)
                |> ignore
            Globals.console.log <| o.ToString ()
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

        cp.stdout.on ("readable", unbox<Function> (cp.stdout.read >> parseResponse )) |> ignore
        cp.stderr.on ("readable", unbox<Function> (cp.stdout.read >> (fun o -> if JS.isDefined o && o <> null then Globals.console.error  <| o.ToString ()) )) |> ignore
        fakeProcess <- Some cp



    let activate (disposables : Disposable[]) =
        let prov = createProvider ()
        registerTextDocumentContentProvider("webpreview", prov) |> ignore

        commands.Globals.registerCommand("webpreview.Show", show |> unbox) |> ignore
        commands.Globals.registerCommand("webpreview.Refresh", (fun _ -> eventEmitter.fire previewUri) |> unbox) |> ignore
        ()
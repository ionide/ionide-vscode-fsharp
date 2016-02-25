namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.languages
open FunScript.TypeScript.child_process
open FunScript.TypeScript.fs

open DTO
open Ionide.VSCode.Helpers

[<ReflectedDefinition>]
module FSharpFormatting =

    [<FunScript.JSEmitInline "(vscode.workspace.registerTextDocumentContentProvider({0}, {1}))">]
    let registerTextDocumentContentProvider(scheme : string, provider : TextDocumentContentProvider) : Disposable = failwith "JS"

    let private path = (VSCode.getPluginPath "Ionide.Ionide-fsharp") + "/bin_ff/FSharpFormattingCLI.exe"
    let private output = (VSCode.getPluginPath "Ionide.Ionide-fsharp") + "/temp/temp.html"
    let private eventEmitter = EventEmitter.Create ()
    let private previewUri = Uri.parse "fsharpformatting://preview"


    let private update = eventEmitter.fire

    let private createProvider () =
        let provider = createEmpty<TextDocumentContentProvider> ()

        let generate () =
            let editor = vscode.window.Globals.activeTextEditor
            let file = editor.document.fileName

            Process.exec path "mono" (sprintf "%s %s" file output)
            |> Promise.success (fun (e,i,o) ->
                fs.Globals.readFileSync output |> fun b ->b.ToString()
            )
            |> Promise.toThenable

        provider.``provideTextDocumentContent <-``((fun _ -> generate ()) |> unbox<_>)
        provider.onDidChange <- eventEmitter.event
        provider

    let show () =
        vscode.commands.Globals.executeCommandOverload2("vscode.previewHtml", previewUri, 2)

    let activate (disposables: Disposable[]) =
        let prov = createProvider ()
        registerTextDocumentContentProvider("fsharpformatting", prov) |> ignore

        workspace.Globals.onDidSaveTextDocument
        |> EventHandler.add (fun _ -> eventEmitter.fire previewUri) () disposables

        commands.Globals.registerCommand("ff.Show", show |> unbox) |> ignore

        ()
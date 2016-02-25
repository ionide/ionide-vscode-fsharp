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

    let generate () =
        let editor = vscode.window.Globals.activeTextEditor
        let file = editor.document.fileName

        Process.exec path "mono" (sprintf "%s %s" file output)
        |> Promise.success (fun (e,i,o) ->
            fs.Globals.readFileSync output |> fun b ->b.ToString()
        )

    let show () =
        generate ()
        |> Promise.bind (fun _ ->
            let uri = Uri.parse( sprintf "file:///%s" output )
            vscode.commands.Globals.executeCommandOverload2("vscode.previewHtml", uri, 2)
            |> Promise.toPromise
        )

    let activate (disposables: Disposable[]) =
        workspace.Globals.onDidSaveTextDocument
        |> EventHandler.add (fun _ -> show ()) () disposables

        commands.Globals.registerCommand("ff.Show", show |> unbox) |> ignore

        ()
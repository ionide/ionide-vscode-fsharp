namespace Ionide.VSCode.FSharp

open System
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers
module node = Fable.Import.Node.Exports

module ScriptRunner =

    let private runFile () =
        let scriptFile = window.activeTextEditor.document.fileName
        let scriptDir = node.path.dirname(scriptFile)

        promise {
            let! fsi =
                LanguageService.fsi ()
                |> Promise.bind (fun p ->
                    match p with
                    | Some fsi -> Promise.lift fsi
                    | None -> Promise.reject "FSI not found"
                )
            let (shellCmd, shellArgs, textToSend) =
                match node.os.``type``() with
                | "Windows_NT" ->
                    ("cmd.exe",
                     [| "/Q"; "/K" |],
                     sprintf "cd \"%s\" && cls && \"%s\" \"%s\" && pause && exit" scriptDir fsi scriptFile)
                | _ ->
                    ("sh",
                     [||],
                     sprintf "cd \"%s\" && clear && \"%s\" \"%s\" && echo \"Press enter to close script...\" && read && exit" scriptDir fsi scriptFile)

            let title = node.path.basename scriptFile
            let terminal = window.createTerminal(title, shellCmd, shellArgs)
            terminal.sendText(textToSend)
            terminal.show ()
        }


    let activate (context : ExtensionContext) =
        commands.registerCommand("fsharp.scriptrunner.run", runFile |> unbox<Func<obj,obj>>) |> context.subscriptions.Add

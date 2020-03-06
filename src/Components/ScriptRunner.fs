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
            let! (fsiBinary, fsiParameters) = Fsi.fsiBinaryAndParameters ()
            let flatArgs  =
                fsiParameters
                |> Array.map (sprintf "\"%s\"")
                |> String.concat " "
            let (shellCmd, shellArgs, textToSend) =
                match node.os.``type``() with
                | "Windows_NT" ->
                    ("cmd.exe",
                     [| "/Q"; "/K" |],
                     sprintf "cd \"%s\" && cls && \"%s\" %s \"%s\" && pause && exit" scriptDir fsiBinary flatArgs scriptFile)
                | _ ->
                    ("sh",
                     [||],
                     sprintf "cd \"%s\" && clear && \"%s\" %s \"%s\" && echo \"Press enter to close script...\" && read && exit" scriptDir fsiBinary flatArgs scriptFile)

            let title = node.path.basename scriptFile
            let terminal = window.createTerminal(title, shellCmd, shellArgs)
            terminal.sendText(textToSend)
            terminal.show ()
        }


    let activate (context : ExtensionContext) =
        commands.registerCommand("fsharp.scriptrunner.run", runFile |> unbox<Func<obj,obj>>) |> context.subscriptions.Add

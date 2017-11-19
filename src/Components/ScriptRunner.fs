namespace Ionide.VSCode.FSharp

open System
open Fable.Import.vscode
open Fable.Import.Node

module ScriptRunner =
    let private runFile () =
        let scriptFile = window.activeTextEditor.document.fileName
        let scriptDir = Path.dirname(scriptFile)

        let (shellCmd, shellArgs, textToSend) =
            match Os.``type``() with
            | "Windows_NT" ->
                ("cmd.exe",
                 [| "/Q"; "/K" |],
                 sprintf "cd \"%s\" && cls && \"%s\" \"%s\" && pause && exit" scriptDir Environment.fsi scriptFile)
            | _ ->
                ("sh",
                 [||],
                 sprintf "cd \"%s\" && clear && \"%s\" \"%s\" && echo \"Press enter to close script...\" && read && exit" scriptDir Environment.fsi scriptFile)

        let title = Path.basename scriptFile
        let terminal = window.createTerminal(title, shellCmd, shellArgs)
        terminal.sendText(textToSend)
        terminal.show ()

    let activate (context: ExtensionContext) =
        commands.registerCommand("fsharp.scriptrunner.run", runFile |> unbox<Func<obj,obj>>) |> context.subscriptions.Add


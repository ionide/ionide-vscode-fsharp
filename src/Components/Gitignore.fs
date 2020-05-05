/// Prompt users to add VSCode lines to their gitignore if the config is set
namespace Ionide.VSCode.FSharp

module Gitignore =

    open Fable.Core
    open Fable.Import.Node
    open Fable.Import.vscode
    open Ionide.VSCode.Helpers
    open Ionide.VSCode.FSharp

    let GITIGNORE_KEY = "FSharp.suggestGitignore"
    let private logger = ConsoleAndOutputChannelLogger(Some "GitIngnore", Level.DEBUG, None, Some Level.DEBUG)
    let gitignorePath () =
        path.join(workspace.workspaceFolders.[0].uri.fsPath , ".gitignore")

    let checkGitignore patterns =
        let patterns = Set.ofSeq patterns
        try
            logger.Debug("gitignore path:", (gitignorePath ()))
            let lines =
                fs.readFileSync(gitignorePath (), "utf8")
                |> String.split [| '\n'; '\r' |]

            (patterns, lines)
            ||> Array.fold (fun notFoundPats line ->
                let line = line.Trim('\t', ' ', '/', '\\')
                if notFoundPats |> Set.contains line
                then notFoundPats |> Set.remove line
                else notFoundPats
            )
        with e -> patterns

    let writePatternsToGitignore patterns =
        let data = patterns |> String.concat System.Environment.NewLine
        fs.appendFileSync(gitignorePath (), System.Environment.NewLine + data, "utf8")

    let disablePromptGlobally () =
        Configuration.setGlobal GITIGNORE_KEY false

    let disablePromptForProject () =
        Configuration.set GITIGNORE_KEY false

    let patternsToIgnore = [
        ".fake"
        ".ionide"
    ]

    let checkForPatternsAndPromptUser () = promise {
        match checkGitignore patternsToIgnore |> Set.toList with
        | [] -> ()
        | missingPatterns ->
            let! choice = window.showInformationMessage("You are missing entries in your .gitignore for Ionide-specific data files. Would you like to add them?", [|"Add entries"; "Ignore"; "Don't show again"|])
            match choice with
            | "Add entries" ->
                writePatternsToGitignore missingPatterns
            | "Ignore" ->
                do! disablePromptForProject ()
            | "Don't show again" ->
                do! disablePromptGlobally ()
            | _ -> ()
    }

    let activate (_context: ExtensionContext) =
        if Configuration.get true GITIGNORE_KEY
        then checkForPatternsAndPromptUser () |> ignore
        else ()



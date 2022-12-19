/// Prompt users to add VSCode lines to their gitignore if the config is set
namespace Ionide.VSCode.FSharp

module Gitignore =

    open Fable.Import.VSCode.Vscode
    open Ionide.VSCode.FSharp

    module node = Node.Api

    let GITIGNORE_KEY = "FSharp.suggestGitignore"

    let private logger =
        ConsoleAndOutputChannelLogger(Some "GitIgnore", Level.DEBUG, None, Some Level.DEBUG)

    let gitignorePath () =
        node.path.join (workspace.workspaceFolders.Value.[0].uri.fsPath, ".gitignore")

    type GitignoreCheckResult =
        | FileNotFound
        | MissingPatterns of string list

    let checkGitignore patterns =
        let patterns = Set.ofSeq patterns

        try
            logger.Debug("gitignore path:", (gitignorePath ()))

            let lines =
                node.fs.readFileSync (gitignorePath (), "utf8") |> String.split [| '\n'; '\r' |]

            (patterns, lines)
            ||> Array.fold (fun notFoundPats line ->
                let line = line.Trim('\t', ' ', '/', '\\')

                if notFoundPats |> Set.contains line then
                    notFoundPats |> Set.remove line
                else
                    notFoundPats)
            |> Set.toList
            |> MissingPatterns
        with ex ->
            logger.Debug("Error accessing gitignore file", ex)
            FileNotFound

    let writePatternsToGitignore patterns =
        let data = patterns |> String.concat System.Environment.NewLine

        node.fs.appendFileSync (gitignorePath (), System.Environment.NewLine + data, "utf8")

    let disablePromptGlobally () =
        Configuration.setGlobal GITIGNORE_KEY (Some(box false))

    let disablePromptForProject () =
        Configuration.set GITIGNORE_KEY (Some(box false))

    let patternsToIgnore = [ ".fake" ]

    let checkForPatternsAndPromptUser () =
        promise {
            match checkGitignore patternsToIgnore with
            | FileNotFound -> ()
            | MissingPatterns [] -> ()
            | MissingPatterns patternsToAdd ->

                let! choice =
                    window.showInformationMessage (
                        "You are missing entries in your .gitignore for Ionide-specific data files. Would you like to add them?",
                        [| Message.choice "Add entries"
                           Message.choice "Ignore"
                           Message.choice "Don't show again" |]
                    )

                match choice with
                | Some(HasTitle "Add entries") -> writePatternsToGitignore patternsToAdd
                | Some(HasTitle "Ignore") -> do! disablePromptForProject ()
                | Some(HasTitle "Don't show again") -> do! disablePromptGlobally ()
                | _ -> ()
        }

    let activate (_context: ExtensionContext) =
        if Configuration.get true GITIGNORE_KEY then
            checkForPatternsAndPromptUser () |> ignore
        else
            ()

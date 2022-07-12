[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open Ionide.VSCode.Helpers
open Ionide.VSCode.FSharp
open Node.ChildProcess

type Api =
    { ProjectLoadedEvent: Event<DTO.Project>
      BuildProject: DTO.Project -> JS.Promise<string>
      BuildProjectFast: DTO.Project -> JS.Promise<string>
      GetProjectLauncher: OutputChannel -> DTO.Project -> (string list -> JS.Promise<ChildProcess>) option
      DebugProject: DTO.Project -> string[] -> JS.Promise<unit> }

let activate (context: ExtensionContext) : JS.Promise<Api> =
    let solutionExplorer = "FSharp.enableTreeView" |> Configuration.get true

    let showExplorer =
        "FSharp.showExplorerOnStartup"
        |> Configuration.get true

    let tryActivate label activationFn =
        fun ctx ->
            try
                activationFn ctx
            with
            | ex ->
                printfn $"Error while activating feature '{label}': {ex}"
                Unchecked.defaultof<_>

    LanguageService.start context
    |> Promise.catch (fun e -> printfn $"Error activating FSAC: %A{e}") // prevent unhandled rejected promises
    |> Promise.onSuccess (fun _ ->
        let progressOpts = createEmpty<ProgressOptions>
        progressOpts.location <- U2.Case1 ProgressLocation.Window

        window.withProgress (
            progressOpts,
            (fun p ctok ->
                let pm =
                    {| message = Some "Loading projects"
                       increment = None |}

                p.report pm

                Project.activate context
                |> Promise.catch (fun e -> printfn $"Error loading projects: %A{e}")
                |> Promise.onSuccess (fun _ -> tryActivate "quickinfoproject" QuickInfoProject.activate context)
                |> Promise.bind (fun _ ->
                    if showExplorer then
                        commands.executeCommand (VSCodeExtension.workbenchViewId ())
                        |> Promise.ofThenable
                    else
                        Promise.lift None)
                |> Promise.bind (fun _ -> tryActivate "analyzers" LanguageService.loadAnalyzers ())
                |> Promise.catch (fun e ->
                    printfn $"Error loading all projects: %A{e}"

                    let pm =
                        {| message = Some "Error loading projects"
                           increment = None |}

                    p.report pm)
                |> Promise.toThenable)
        )
        |> ignore)
    |> Promise.map (fun _ ->
        if solutionExplorer then
            tryActivate "solutionExplorer" SolutionExplorer.activate context

        tryActivate "fsprojedit" FsProjEdit.activate context
        tryActivate "diagnostics" Diagnostics.activate context
        tryActivate "linelens" LineLens.activate context
        tryActivate "quickinfo" QuickInfo.activate context
        tryActivate "help" Help.activate context
        tryActivate "msbuild" MSBuild.activate context
        tryActivate "signaturedata" SignatureData.activate context
        tryActivate "debugger" Debugger.activate context
        tryActivate "fsdn" Fsdn.activate context
        tryActivate "fsi" Fsi.activate context
        tryActivate "scriptrunner" ScriptRunner.activate context
        tryActivate "languageconfiguration" LanguageConfiguration.activate context
        tryActivate "htmlconverter" HtmlConverter.activate context
        tryActivate "infopanel" InfoPanel.activate context
        tryActivate "codelens" CodeLensHelpers.activate context
        // tryActivate "faketargetsoutline" FakeTargetsOutline.activate context
        tryActivate "gitignore" Gitignore.activate context
        tryActivate "fsharpliterate" FSharpLiterate.activate context
        tryActivate "pipelinehints" PipelineHints.activate context
        tryActivate "testExplorer" TestExploer.activate context
        tryActivate "inlayhints" InlayHints.activate context

        let buildProject project =
            promise {
                let! exit = MSBuild.buildProjectPath "Build" project

                match exit.Code with
                | Some code -> return code.ToString()
                | None -> return ""
            }

        let buildProjectFast project =
            promise {
                let! exit = MSBuild.buildProjectPathFast project

                match exit.Code with
                | Some code -> return code.ToString()
                | None -> return ""
            }

        let event = vscode.EventEmitter.Create<DTO.Project>()

        Project.projectLoaded.Invoke(fun n -> !!(setTimeout (fun _ -> event.fire n) 500.))
        |> ignore

        { ProjectLoadedEvent = event.event
          BuildProject = buildProject
          BuildProjectFast = buildProjectFast
          GetProjectLauncher = Project.getLauncher
          DebugProject = Debugger.debugProject })
    |> Promise.catch (fun e ->
        printfn $"Error activating features: %A{e}"
        Unchecked.defaultof<_>)


let deactivate (disposables: Disposable[]) = LanguageService.stop ()

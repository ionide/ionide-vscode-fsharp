[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.vscode
open Ionide.VSCode.Helpers
open Ionide.VSCode.FSharp
open Fable.Import.Node.ChildProcess
open Debugger

type Api = {
    ProjectLoadedEvent: Event<DTO.Project>
    BuildProject: DTO.Project -> Fable.Import.JS.Promise<string>
    BuildProjectFast: DTO.Project -> Fable.Import.JS.Promise<string>
    GetProjectLauncher: OutputChannel -> DTO.Project -> (string -> Fable.Import.JS.Promise<ChildProcess>) option
    DebugProject: DTO.Project -> string [] -> Fable.Import.JS.Promise<unit>
}

let activate (context: ExtensionContext) : Api =
    let df = createEmpty<DocumentFilter>
    df.language <- Some "fsharp"
    let df' : DocumentSelector = df |> U3.Case2

    let resolve = "FSharp.resolveNamespaces" |> Configuration.get false
    let solutionExplorer = "FSharp.enableTreeView" |> Configuration.get true

    let backgroundSymbolCache = "FSharp.enableBackgroundSymbolCache" |> Configuration.get false

    let init = DateTime.Now

    LanguageService.start ()
    |> Promise.onSuccess (fun _ ->
        let progressOpts = createEmpty<ProgressOptions>
        progressOpts.location <- ProgressLocation.Window
        window.withProgress(progressOpts, (fun p ->
            let pm = createEmpty<ProgressMessage>
            pm.message <- "Loading current project"
            p.report pm
            LineLens.activate context
            Errors.activate context
            |> Promise.onSuccess(fun _ ->
                pm.message <- "Loading all projects"
                p.report pm
                CodeLens.activate df' context
                Linter.activate df' context
                UnusedOpens.activate df' context
                UnusedDeclarations.activate df' context
                SimplifyName.activate df' context
                CodeOutline.activate context

            )
            |> Promise.onSuccess(fun _ -> if solutionExplorer then SolutionExplorer.activate context)
            |> Promise.bind(fun parseVisibleTextEditors -> Project.activate context parseVisibleTextEditors)
            |> Promise.onSuccess(fun _ -> QuickInfoProject.activate context )

        ))
        |> Promise.onSuccess (fun _ ->
            let e = DateTime.Now - init
            printfn "Startup took: %f ms" e.TotalMilliseconds
        )
        |> Promise.onSuccess (fun _ ->
            if backgroundSymbolCache then
                let progressOpts = createEmpty<ProgressOptions>
                progressOpts.location <- ProgressLocation.Window
                window.withProgress(progressOpts, (fun p ->
                    let pm = createEmpty<ProgressMessage>
                    pm.message <- "Building background symbol cache"
                    p.report pm
                    LanguageService.enableSymbolCache ()
                    |> Promise.bind (fun _ -> LanguageService.buildBackgroundSymbolCache ())
                )) |> ignore
        )
        |> ignore

        Tooltip.activate df' context
        Autocomplete.activate df' context
        ParameterHints.activate df' context
        Definition.activate df' context
        TypeDefinition.activate df' context
        Reference.activate df' context
        Symbols.activate df' context
        Highlights.activate df' context
        Rename.activate df' context
        WorkspaceSymbols.activate df' context
        QuickInfo.activate context
        QuickFix.activate df' context
        if resolve then ResolveNamespaces.activate df' context
        UnionCaseGenerator.activate df' context
        RecordStubGenerator.activate df' context
        Help.activate context
        MSBuild.activate context
        SignatureData.activate context
        Debugger.activate context
        Diagnostics.activate context
    )
    |> Promise.catch (fun error -> promise { () }) // prevent unhandled rejected promises
    |> ignore

    Forge.activate context
    Fsi.activate context
    ScriptRunner.activate context
    LanguageConfiguration.activate context
    HtmlConverter.activate context

    let buildProject project = promise {
        let! exit = MSBuild.buildProjectPath "Build" project
        match exit.Code with
        | Some code -> return code.ToString()
        | None -> return ""
    }

    let buildProjectFast project = promise {
        let! exit = MSBuild.buildProjectPathFast project
        match exit.Code with
        | Some code -> return code.ToString()
        | None -> return ""
    }

    let event = Fable.Import.vscode.EventEmitter<DTO.Project>()
    Project.projectLoaded.event.Invoke(fun n ->
        !!(setTimeout (fun _ -> event.fire n) 500.)
    ) |> ignore

    { ProjectLoadedEvent = event.event
      BuildProject = buildProject
      BuildProjectFast = buildProjectFast
      GetProjectLauncher = Project.getLauncher
      DebugProject = debugProject }

let deactivate(disposables: Disposable[]) =
    LanguageService.stop ()
    ()

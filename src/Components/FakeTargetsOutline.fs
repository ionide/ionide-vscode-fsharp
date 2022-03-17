namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open global.Node
open Ionide.VSCode.Helpers
open System.Collections.Generic

open DTO
open DTO.FakeSupport

module node = Node.Api

module FakeTargetsOutline =

    let private configurationKey = "FAKE.targetsOutline"

    let private isEnabledFor (uri: Uri) =
        configurationKey
        |> Configuration.getInContext uri true

    type NodeEntry =
        { Key: string
          Children: Dictionary<string, NodeEntry>
          Symbol: Symbol }

    type DependencyType =
        | SoftDependency
        | HardDependency
        member x.Arrow =
            match x with
            | SoftDependency -> "<=?"
            | HardDependency -> "<=="

    type ModelType =
        | TargetModel
        | ErrorOrWarning
        | DependencyModel of DependencyType

    type Model =
        { AllTargets: Target []
          Label: string
          Description: string
          Declaration: Declaration option
          Type: ModelType
          getChildren: unit -> ResizeArray<Model> }
        member x.IsTarget =
            match x.Type with
            | TargetModel -> true
            | _ -> false

    let refresh = vscode.EventEmitter.Create<Uri>()
    let private reallyRefresh = vscode.EventEmitter.Create<U2<Model, unit> option>()
    let mutable private currentDocument: string option = None

    let private getIconPath light dark =
        let plugPath = VSCodeExtension.ionidePluginPath ()

        {| dark =
            node.path.join (plugPath, "images", dark)
            |> U2.Case1

           light =
            node.path.join (plugPath, "images", light)
            |> U2.Case1 |}

    let rec add' (state: NodeEntry) (symbol: Symbol) index =
        let sep = "."

        let entry = symbol.Name

        if index >= entry.Length then
            state
        else
            let endIndex = entry.IndexOf(sep, index)

            let endIndex =
                if endIndex = -1 then
                    entry.Length
                else
                    endIndex

            let key = entry.Substring(index, endIndex - index)

            if String.IsNullOrEmpty key then
                state
            else
                if state.Children.ContainsKey key |> not then
                    let x =
                        { Key = key
                          Children = new Dictionary<_, _>()
                          Symbol = symbol }

                    state.Children.Add(key, x)

                let item = state.Children.[key]
                add' item symbol (endIndex + 1)

    let getIcon (node: Model) =
        // TODO use other icon for dependencies
        match node.Type with
        | ModelType.ErrorOrWarning ->
            if node.AllTargets.Length = 0 then // error
                Some
                <| getIconPath "error-inverse.svg" "error.svg"
            else
                Some
                <| getIconPath "warning-inverse.svg" "warning.svg"
        | ModelType.TargetModel ->
            Some
            <| getIconPath "icon-function-light.svg" "icon-function-dark.svg"
        | ModelType.DependencyModel _ ->
            Some
            <| getIconPath "auto-reveal-light.svg" "auto-reveal-dark.svg"

    let tryFindTarget (allTargets: Target []) (name: string) =
        allTargets
        |> Seq.tryFind (fun t -> t.Name.ToLowerInvariant() = name.ToLowerInvariant())

    let rec depAsModel (allTargets: Target []) (t: DependencyType) (d: Dependency) =
        let mutable children = None

        { AllTargets = allTargets
          Label = t.Arrow + " " + d.Name
          Description = "A fake dependency"
          Declaration =
            if isNull d.Declaration.File then
                None
            else
                Some d.Declaration
          Type = ModelType.DependencyModel t
          getChildren =
            fun () ->
                match children with
                | Some s -> s
                | None ->
                    let n =
                        tryFindTarget allTargets d.Name
                        |> Option.map (targetAsModel allTargets)
                        |> Option.toArray
                        |> ResizeArray

                    children <- Some n
                    n }

    and targetAsModel (allTargets: Target []) (t: Target) =
        let mutable children = None

        { AllTargets = allTargets
          Label = t.Name
          Description = t.Description
          Declaration =
            if isNull t.Declaration.File then
                None
            else
                Some t.Declaration
          Type = ModelType.TargetModel
          getChildren =
            fun () ->
                match children with
                | Some s -> s
                | None ->
                    let n =
                        let hard =
                            t.HardDependencies
                            |> Seq.map (depAsModel allTargets DependencyType.HardDependency)

                        let soft =
                            t.SoftDependencies
                            |> Seq.map (depAsModel allTargets DependencyType.SoftDependency)

                        Seq.append hard soft |> ResizeArray

                    children <- Some n
                    n }

    let generateErrorEntry targets msg =
        { AllTargets = targets
          Label = msg
          Description = msg
          Declaration = None
          Type = ModelType.ErrorOrWarning
          getChildren = fun () -> ResizeArray() }

    let generateErrorRoot msg : ResizeArray<Model> =
        [ generateErrorEntry [||] msg ] |> ResizeArray

    let generateRootFromResponse (o: GetTargetsResult) : ResizeArray<Model> =
        let items =
            o.WarningsAndErrors
            |> Seq.map (function
                | GetTargetsWarningOrErrorType.NoFakeScript ->
                    generateErrorEntry o.Targets "this script is not a FAKE script"
                | GetTargetsWarningOrErrorType.MissingFakeCoreTargets ->
                    generateErrorEntry o.Targets "this script does not use Fake.Core.Target"
                | GetTargetsWarningOrErrorType.FakeCoreTargetsOlderThan5_15 ->
                    generateErrorEntry
                        o.Targets
                        (sprintf "this script should be updated to at least Fake.Core.Target 5.15")
                | GetTargetsWarningOrErrorType.MissingNavigationInfo ->
                    generateErrorEntry
                        o.Targets
                        "navigation is missing, are you missing 'Target.initEnvironment()` at the top?"
                | code -> generateErrorEntry o.Targets (sprintf "unknown error code %d" (int code)))

        let realItems = o.Targets |> Seq.map (targetAsModel o.Targets)

        [ yield! items; yield! realItems ] |> ResizeArray

    let createProvider () : TreeDataProvider<Model> =
        let mutable e = Some reallyRefresh.event

        { new TreeDataProvider<Model> with
            override this.getChildren(element: Model option) : ProviderResult<ResizeArray<Model>> =
                match element with
                | Some node -> node.getChildren () |> U2.Case1 |> Some
                | None ->
                    let doc = window.activeTextEditor

                    match doc with
                    | Some doc ->
                        match currentDocument with
                        | None -> currentDocument <- Some doc.document.fileName
                        | Some path ->
                            if path <> doc.document.fileName then
                                currentDocument <- Some doc.document.fileName

                        match doc.document with
                        | Document.FSharp ->
                            promise {
                                let! o = LanguageService.FakeSupport.targetsInfo doc.document.fileName

                                if isNotNull o then
                                    return generateRootFromResponse o
                                else
                                    return generateErrorRoot "null response from fsac"
                            }
                            |> unbox
                        | _ ->
                            generateErrorRoot "No active F# document"
                            |> U2.Case1
                            |> Some
                    | None ->
                        generateErrorRoot "No active document"
                        |> U2.Case1
                        |> Some

            override this.getParent(element: Model) : ProviderResult<Model> = None

            override this.getTreeItem(element: Model) : U2<TreeItem, Thenable<TreeItem>> =
                let children = element.getChildren ()

                let state =
                    if JS.isDefined children && children.Count > 0 then
                        Some TreeItemCollapsibleState.Collapsed
                    else
                        None

                let ti = createEmpty<TreeItem>
                ti.label <- Some(U2.Case1 element.Label)
                ti.collapsibleState <- state
                ti.iconPath <- getIcon element |> Option.map U4.Case3

                ti.contextValue <-
                    Some(
                        match element.Type with
                        | ModelType.TargetModel -> "fake.targetsOutline.target"
                        | ModelType.DependencyModel _ -> "fake.targetsOutline.dependency"
                        | ModelType.ErrorOrWarning -> "fake.targetsOutline.error"
                    )

                ti.tooltip <- Some(U2.Case1 element.Description)

                let c = createEmpty<Command>
                c.command <- "FAKE.targetsOutline.goTo"
                c.title <- "open"
                c.arguments <- Some(ResizeArray [| unbox element |])
                ti.command <- Some c
                U2.Case1 ti

            member this.onDidChangeTreeData: Event<U2<Model, unit> option> option = e

            member this.onDidChangeTreeData
                with set (v: Event<U2<Model, unit> option> option): unit = e <- v

            override this.resolveTreeItem
                (
                    item: TreeItem,
                    element: Model,
                    token: CancellationToken
                ) : ProviderResult<TreeItem> =
                failwith "Not Implemented" }

    module private ShowInActivity =
        let private setInFsharpActivity =
            Context.cachedSetter<bool> "fake.targetsOutline.showInFsharpActivity"

        let private setInExplorerActivity =
            Context.cachedSetter<bool> "fake.targetsOutline.showInExplorerActivity"

        let showInFsharpActivity () =
            let showIn =
                "FAKE.showTargetsOutlineIn"
                |> Configuration.get "explorer"

            showIn = "fsharp"

        let initializeAndGetId () =
            let inFsharpActivity = showInFsharpActivity ()
            setInFsharpActivity inFsharpActivity
            setInExplorerActivity (not inFsharpActivity)

            if inFsharpActivity then
                "fake.targetsOutlineInActivity"
            else
                "fake.targetsOutline"

    let setShowCodeOutline = Context.cachedSetter<bool> "fake.targetsOutline.show"

    let inline private isFsharpFile (doc: TextDocument) =
        match doc with
        | Document.FSharp when doc.uri.scheme = "file" -> true
        | _ -> false

    let private setShowTargetsOutlineForEditor (textEditor: TextEditor option) =
        let newValue =
            match textEditor with
            | Some textEditor ->
                if isFsharpFile textEditor.document
                   || ShowInActivity.showInFsharpActivity () then
                    isEnabledFor textEditor.document.uri
                else
                    false
            | None -> false

        setShowCodeOutline newValue

    let onDidChangeConfiguration (evt: ConfigurationChangeEvent) =
        let textEditor = window.activeTextEditor

        match textEditor with
        | Some textEditor ->
            if evt.affectsConfiguration (configurationKey, ConfigurationScope.Case1 textEditor.document.uri) then
                setShowTargetsOutlineForEditor window.activeTextEditor
                refresh.fire textEditor.document.uri
        | None -> ()

    let private onDidChangeActiveTextEditor (textEditor: TextEditor option) =
        setShowTargetsOutlineForEditor textEditor

        match textEditor with
        | Some textEditor ->
            if not (isFsharpFile textEditor.document) then
                reallyRefresh.fire (None)
        | None -> ()


    type RequestLaunch =
        { name: string
          ``type``: string
          request: string
          preLaunchTask: string option
          program: string
          args: string array
          cwd: string
          console: string
          stopAtEntry: bool
          justMyCode: bool
          requireExactSource: bool }

    let activate (context: ExtensionContext) =
        setShowTargetsOutlineForEditor window.activeTextEditor

        let treeViewId = ShowInActivity.initializeAndGetId ()

        window.onDidChangeActiveTextEditor.Invoke(unbox onDidChangeActiveTextEditor)
        |> context.Subscribe

        refresh.event.Invoke (fun uri ->
            if isEnabledFor uri then
                reallyRefresh.fire (None)

            createEmpty)
        |> context.Subscribe

        workspace.onDidChangeConfiguration.Invoke(unbox onDidChangeConfiguration)
        |> context.Subscribe

        commands.registerCommand (
            "FAKE.targetsOutline.goTo",
            objfy2 (fun n ->
                let m = unbox<Model> n

                match m.Declaration with
                | Some decl ->
                    let line = decl.Line

                    let args =
                        createObj [ "lineNumber" ==> line
                                    "at" ==> "center" ]

                    commands.executeCommand ("revealLine", Some(box args))
                    |> unbox
                | None -> JS.undefined)
        )
        |> context.Subscribe

        let runFake doDebug onlySingleTarget targetName =
            promise {
                match currentDocument with
                | Some scriptFile ->
                    let scriptDir = node.path.dirname scriptFile
                    let scriptName = node.path.basename scriptFile
                    let! fakeRuntime = LanguageService.FakeSupport.fakeRuntime ()
                    let preArg = if onlySingleTarget then "-st" else "-t"

                    let args =
                        if doDebug then
                            [| "run"
                               "--nocache"
                               "--fsiargs"
                               "--debug:portable --optimize-"
                               scriptName
                               preArg
                               targetName |]
                        else
                            [| "run"
                               scriptName
                               preArg
                               targetName |]

                    let cfg: RequestLaunch =
                        { name = "Fake Script Debugging"
                          ``type`` = "coreclr"
                          request = "launch"
                          preLaunchTask = None
                          program = fakeRuntime
                          args = args
                          cwd = scriptDir
                          console = "externalTerminal"
                          stopAtEntry = false
                          justMyCode = false
                          requireExactSource = false }

                    if doDebug then
                        let! res = debug.startDebugging (JS.undefined, unbox cfg)
                        ()
                    else
                        let! dotnet = LanguageService.dotnet ()

                        match dotnet with
                        | None ->
                            let! _ =
                                window.showErrorMessage (
                                    "Cannot start fake as no dotnet runtime was found. Consider configuring one in ionide settings.",
                                    null
                                )

                            ()
                        | Some dotnet ->
                            let taskDef =
                                let data = Dictionary()

                                { new TaskDefinition with
                                    member this.Item
                                        with get (name: string): obj option = data.TryGet name

                                        and set (name: string) (v: obj option): unit =
                                            match v with
                                            | None -> data.Remove(name) |> ignore
                                            | Some v -> data.[name] <- v

                                    override this.``type``: string = "fakerun" }

                            let opts = createEmpty<ProcessExecutionOptions>
                            opts.cwd <- Some cfg.cwd

                            let procExp =
                                vscode.ProcessExecution.Create(
                                    dotnet,
                                    ResizeArray [| yield fakeRuntime
                                                   yield! args |],
                                    opts
                                )

                            let task =
                                vscode.Task.Create(
                                    taskDef,
                                    U2.Case2 TaskScope.Global,
                                    "fake run",
                                    "fake",
                                    U3.Case1 procExp
                                )

                            let exec = tasks.executeTask (task)
                            ()
                | None ->
                    let! _ = window.showErrorMessage ("Cannot start fake as no script file is selected.")
                    ()
            }

        let debugTarget onlySingleTarget targetName =
            runFake true onlySingleTarget targetName :> obj

        let runTarget onlySingleTarget targetName =
            runFake false onlySingleTarget targetName :> obj

        commands.registerCommand (
            "fake.targetsOutline.reloadTargets",
            objfy2 (fun _ -> refresh.fire undefined |> unbox)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fake.targetsOutline.runTarget",
            objfy2 (fun n ->
                let item = unbox<Model> n
                runTarget false item.Label)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fake.targetsOutline.debugTarget",
            objfy2 (fun n ->
                let item = unbox<Model> n
                debugTarget false item.Label)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fake.targetsOutline.runSingleTarget",
            objfy2 (fun n ->
                let item = unbox<Model> n
                runTarget true item.Label)
        )
        |> context.Subscribe

        commands.registerCommand (
            "fake.targetsOutline.debugSingleTarget",
            objfy2 (fun n ->
                let item = unbox<Model> n
                debugTarget true item.Label)
        )
        |> context.Subscribe

        let provider = createProvider ()
        let treeOptions = createEmpty<TreeViewOptions<Model>>
        treeOptions.treeDataProvider <- provider
        treeOptions.showCollapseAll <- Some true
        let treeView = window.createTreeView (treeViewId, treeOptions)
        context.Subscribe treeView

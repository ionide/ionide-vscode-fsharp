namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers
open System.Collections.Generic

open DTO
open DTO.FakeSupport
open Ionide.VSCode.Helpers
module node = Fable.Import.Node.Exports

module FakeTargetsOutline =

    let private configurationKey = "FAKE.targetsOutline"
    let private isEnabledFor uri = configurationKey |> Configuration.getInContext uri true

    type NodeEntry =
        { Key : string
          Children : Dictionary<string, NodeEntry>
          Symbol : Symbol }

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
        { AllTargets : Target []
          Label : string
          Description : string
          Declaration : Declaration option
          Type : ModelType
          getChildren : unit -> ResizeArray<Model> }
        member x.IsTarget =
            match x.Type with
            | TargetModel -> true
            | _ -> false

    let refresh = EventEmitter<Uri> ()
    let private reallyRefresh = EventEmitter<Model option> ()
    let mutable private currentDocument : string option = None

    let private getIconPath light dark =
        let plugPath =
            try
                (VSCode.getPluginPath "Ionide.ionide-fsharp")
            with
            | _ ->  (VSCode.getPluginPath "Ionide.Ionide-fsharp")

        let p = createEmpty<TreeIconPath>
        p.dark <- node.path.join(plugPath, "images", dark) |> U3.Case1
        p.light <- node.path.join(plugPath, "images", light) |> U3.Case1
        p

    let rec add' (state : NodeEntry) (symbol : Symbol) index =
        let sep = "."

        let entry = symbol.Name

        if index >= entry.Length then
            state
        else
            let endIndex = entry.IndexOf(sep, index)
            let endIndex = if endIndex = -1 then entry.Length else endIndex

            let key = entry.Substring(index, endIndex - index)
            if String.IsNullOrEmpty key then
                state
            else
                if state.Children.ContainsKey key |> not then
                    let x = {Key = key; Children = new Dictionary<_,_>(); Symbol = symbol}
                    state.Children.Add(key,x)
                let item = state.Children.[key]
                add' item symbol (endIndex + 1)

    let getIcon (node:Model) =
        // TODO use other icon for dependencies
        match node.Type with
        | ModelType.ErrorOrWarning ->
            if node.AllTargets.Length = 0 then // error
                Some <| getIconPath "error-inverse.svg" "error.svg"
            else
                Some <| getIconPath "warning-inverse.svg" "warning.svg"
        | ModelType.TargetModel ->
            Some <| getIconPath "icon-function-light.svg" "icon-function-dark.svg"
        | ModelType.DependencyModel _ ->
            Some <| getIconPath "auto-reveal-light.svg" "auto-reveal-dark.svg"

    let tryFindTarget (allTargets:Target[]) (name:string)=
        allTargets |> Seq.tryFind (fun t -> t.Name.ToLowerInvariant() = name.ToLowerInvariant())

    let rec depAsModel (allTargets:Target[]) (t:DependencyType) (d:Dependency) =
        let mutable children = None
        {
            AllTargets = allTargets
            Label = t.Arrow + " " + d.Name
            Description = "A fake dependency"
            Declaration = if isNull d.Declaration.File then None else Some d.Declaration
            Type = ModelType.DependencyModel t
            getChildren = fun () ->
                match children with
                | Some s -> s
                | None ->
                    let n =
                        tryFindTarget allTargets d.Name
                        |> Option.map (targetAsModel allTargets)
                        |> Option.toArray
                        |> ResizeArray
                    children <- Some n
                    n
        }
    and targetAsModel (allTargets:Target[]) (t:Target) =
        let mutable children = None
        {
            AllTargets = allTargets
            Label = t.Name
            Description = t.Description
            Declaration = if isNull t.Declaration.File then None else Some t.Declaration
            Type = ModelType.TargetModel
            getChildren = fun () ->
                match children with
                | Some s -> s
                | None ->
                    let n =
                        let hard = t.HardDependencies |> Seq.map (depAsModel allTargets DependencyType.HardDependency)
                        let soft = t.SoftDependencies |> Seq.map (depAsModel allTargets DependencyType.SoftDependency)
                        Seq.append hard soft
                        |> ResizeArray
                    children <- Some n
                    n
        }

    let generateErrorEntry targets msg =
        {
            AllTargets = targets
            Label = msg
            Description = msg
            Declaration = None
            Type = ModelType.ErrorOrWarning
            getChildren = fun () -> ResizeArray()
        }

    let generateErrorRoot msg : ResizeArray<Model> =
        [ generateErrorEntry [||] msg ]
        |> ResizeArray

    let generateRootFromResponse (o:GetTargetsResult) : ResizeArray<Model> =
        let items =
            o.WarningsAndErrors
            |> Seq.map (function
                | GetTargetsWarningOrErrorType.NoFakeScript -> generateErrorEntry o.Targets "this script is not a FAKE script"
                | GetTargetsWarningOrErrorType.MissingFakeCoreTargets -> generateErrorEntry o.Targets "this script does not use Fake.Core.Target"
                | GetTargetsWarningOrErrorType.FakeCoreTargetsOlderThan5_15 -> generateErrorEntry o.Targets (sprintf "this script should be updated to at least Fake.Core.Target 5.15")
                | GetTargetsWarningOrErrorType.MissingNavigationInfo -> generateErrorEntry o.Targets "navigation is missing, are you missing 'Target.initEnvironment()` at the top?"
                | code -> generateErrorEntry o.Targets (sprintf "unknown error code %d" (int code)))
        let realItems =
            o.Targets
            |> Seq.map (targetAsModel o.Targets)

        [ yield! items; yield! realItems] |> ResizeArray

    let createProvider () : TreeDataProvider<Model> =
        { new TreeDataProvider<Model> with
            member __.getParent =
                None

            member this.onDidChangeTreeData =
                reallyRefresh.event

            member this.getChildren(node) =
                if JS.isDefined node then
                    node.getChildren()
                else
                    let doc = window.activeTextEditor
                    if JS.isDefined doc
                    then
                        match currentDocument with
                        | None ->
                            currentDocument <- Some doc.document.fileName
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
                            } |> unbox
                        | _ -> generateErrorRoot "No active F# document"
                    else generateErrorRoot "No active document"

            member this.getTreeItem(node) =
                let children = node.getChildren()
                let state =
                    if JS.isDefined children && children.Count > 0 then
                        Some TreeItemCollapsibleState.Collapsed
                    else None

                let ti = createEmpty<TreeItem>
                ti.label <- Some node.Label //getLabel node |> Some
                ti.collapsibleState <- state
                ti.iconPath <- getIcon node |> Option.map U4.Case3
                ti.contextValue <-
                    Some (match node.Type with
                          | ModelType.TargetModel -> "fake.targetsOutline.target"
                          | ModelType.DependencyModel _ -> "fake.targetsOutline.dependency"
                          | ModelType.ErrorOrWarning -> "fake.targetsOutline.error")
                ti.tooltip <- Some node.Description

                let c = createEmpty<Command>
                c.command <- "FAKE.targetsOutline.goTo"
                c.title <- "open"
                c.arguments <- Some (ResizeArray [| unbox node|])
                ti.command <- Some c
                ti
        }

    module private ShowInActivity =
        let private setInFsharpActivity = Context.cachedSetter<bool> "fake.targetsOutline.showInFsharpActivity"
        let private setInExplorerActivity = Context.cachedSetter<bool> "fake.targetsOutline.showInExplorerActivity"

        let showInFsharpActivity () =
            let showIn = "FAKE.showTargetsOutlineIn" |> Configuration.get "explorer"
            showIn = "fsharp"

        let initializeAndGetId () =
            let inFsharpActivity = showInFsharpActivity ()
            setInFsharpActivity inFsharpActivity
            setInExplorerActivity (not inFsharpActivity)

            if inFsharpActivity then "fake.targetsOutlineInActivity" else "fake.targetsOutline"

    let setShowCodeOutline = Context.cachedSetter<bool> "fake.targetsOutline.show"

    let inline private isFsharpFile (doc : TextDocument) =
        match doc with
        | Document.FSharp when doc.uri.scheme = "file" -> true
        | _ -> false

    let private setShowTargetsOutlineForEditor (textEditor : TextEditor) =
        let newValue =
            if textEditor <> undefined then
                if isFsharpFile textEditor.document || ShowInActivity.showInFsharpActivity() then
                    isEnabledFor textEditor.document.uri
                else
                    false
            else
                false
        setShowCodeOutline newValue

    let onDidChangeConfiguration (evt : ConfigurationChangeEvent) =
        let textEditor = window.activeTextEditor
        if textEditor <> undefined && evt.affectsConfiguration(configurationKey, textEditor.document.uri) then
            setShowTargetsOutlineForEditor window.activeTextEditor
            refresh.fire textEditor.document.uri

    let private onDidChangeActiveTextEditor (textEditor : TextEditor) =
        setShowTargetsOutlineForEditor textEditor
        if textEditor = undefined || (not (isFsharpFile textEditor.document)) then
            reallyRefresh.fire(None)


    type [<Pojo>] RequestLaunch =
        { name : string
          ``type`` : string
          request : string
          preLaunchTask : string option
          program : string
          args : string array
          cwd : string
          console : string
          stopAtEntry : bool
          justMyCode : bool
          requireExactSource : bool }

    let activate (context : ExtensionContext) =
        setShowTargetsOutlineForEditor window.activeTextEditor

        let treeViewId = ShowInActivity.initializeAndGetId ()

        window.onDidChangeActiveTextEditor.Invoke(unbox onDidChangeActiveTextEditor)
            |> context.subscriptions.Add

        refresh.event.Invoke(fun uri ->
            if isEnabledFor uri then
                reallyRefresh.fire(None)
            createEmpty)
            |> context.subscriptions.Add

        workspace.onDidChangeConfiguration.Invoke(unbox onDidChangeConfiguration)
            |> context.subscriptions.Add

        commands.registerCommand("FAKE.targetsOutline.goTo", Func<obj, obj>(fun n ->
            let m = unbox<Model> n
            match m.Declaration with
            | Some decl ->
                let line = decl.Line
                let args =
                    createObj [
                        "lineNumber" ==> line
                        "at" ==> "center"
                    ]

                vscode.commands.executeCommand("revealLine", args)
                |> unbox
            | None -> JS.undefined
        )) |> context.subscriptions.Add

        let runFake doDebug onlySingleTarget targetName =
            promise {
                match currentDocument with
                | Some scriptFile ->
                    let scriptDir = node.path.dirname scriptFile
                    let scriptName = node.path.basename scriptFile
                    let! fakeRuntime = LanguageService.FakeSupport.fakeRuntime()
                    let preArg = if onlySingleTarget then "-st" else "-t"
                    let args =
                        if doDebug then [| "run"; "--nocache"; "--fsiargs"; "--debug:portable --optimize-"; scriptName; preArg; targetName |]
                        else [| "run"; scriptName; preArg; targetName |]
                    let cfg : RequestLaunch =
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
                        let! res = debug.startDebugging(JS.undefined, unbox cfg)
                        ()
                    else
                        let! dotnet = LanguageService.dotnet()
                        match dotnet with
                        | None ->
                            let! _ = window.showErrorMessage("Cannot start fake as no dotnet runtime was found. Consider configuring one in ionide settings.")
                            ()
                        | Some dotnet ->
                            let taskDef = createEmpty<TaskDefinition>
                            taskDef.``type`` <- Some "fakerun"
                            let opts = createEmpty<ProcessExecutionOptions>
                            opts.cwd <- Some cfg.cwd
                            let procExp = ProcessExecution(dotnet, [| yield fakeRuntime; yield! args |], opts)
                            let task = Task(taskDef, ConfigurationTarget.Global, "fake run", "fake", procExp)
                            let exec = tasks.executeTask(task)
                            ()
                | None ->
                    let! _ = window.showErrorMessage("Cannot start fake as no script file is selected.")
                    ()
            }

        let debugTarget onlySingleTarget targetName =
            runFake true onlySingleTarget targetName :> obj

        let runTarget onlySingleTarget targetName =
            runFake false onlySingleTarget targetName :> obj

        commands.registerCommand("fake.targetsOutline.reloadTargets", Func<obj, obj>(fun _ ->
            refresh.fire undefined |> unbox
        )) |> context.subscriptions.Add
        commands.registerCommand("fake.targetsOutline.runTarget", Func<obj, obj>(fun n ->
            let item = unbox<Model> n
            runTarget false item.Label
        )) |> context.subscriptions.Add
        commands.registerCommand("fake.targetsOutline.debugTarget", Func<obj, obj>(fun n ->
            let item = unbox<Model> n
            debugTarget false item.Label
        )) |> context.subscriptions.Add
        commands.registerCommand("fake.targetsOutline.runSingleTarget", Func<obj, obj>(fun n ->
            let item = unbox<Model> n
            runTarget true item.Label
        )) |> context.subscriptions.Add
        commands.registerCommand("fake.targetsOutline.debugSingleTarget", Func<obj, obj>(fun n ->
            let item = unbox<Model> n
            debugTarget true item.Label
        )) |> context.subscriptions.Add

        let provider = createProvider ()
        let treeOptions = createEmpty<CreateTreeViewOptions<Model>>
        treeOptions.treeDataProvider <- provider
        treeOptions.showCollapseAll <- Some true
        let treeView = window.createTreeView(treeViewId, treeOptions)
        context.subscriptions.Add treeView

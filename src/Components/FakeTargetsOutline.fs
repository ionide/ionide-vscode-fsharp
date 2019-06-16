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
        | DependencyModel of DependencyType

    type Model =
        { AllTargets : Target []
          Label : string
          Description : string
          Declaration : Declaration
          Type : ModelType
          getChildren : unit -> ResizeArray<Model> }      

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

    let getRoot (doc : TextDocument) =
        promise {
            let! o = LanguageService.FakeSupport.targetsInfo doc.fileName
            if isNotNull o then
                return o.Data
            else
                return unbox null
        }

    let getIcon node =
        // TODO use other icon for dependencies
        Some <| getIconPath "icon-function-light.svg" "icon-function-dark.svg"

    let tryFindTarget (allTargets:Target[]) (name:string)=
        allTargets |> Seq.tryFind (fun t -> t.Name.ToLowerInvariant() = name.ToLowerInvariant())

    let rec depAsModel (allTargets:Target[]) (t:DependencyType) (d:Dependency) =
        let mutable children = None
        {
            AllTargets = allTargets
            Label = t.Arrow + " " + d.Name
            Description = "A fake dependency"
            Declaration = d.Declaration
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
            Declaration = t.Declaration
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
                            currentDocument <- Some doc.document.uri.path
                        | Some path ->
                            if path <> doc.document.uri.path then
                                currentDocument <- Some doc.document.uri.path

                        match doc.document with
                        | Document.FSharp ->
                            promise {                        
                                let! x = getRoot doc.document
                                return 
                                    if isNotNull x then
                                        x
                                        |> Seq.map (targetAsModel x)
                                        |> ResizeArray
                                    else unbox (ResizeArray ())
                            } |> unbox
                        | _ ->  ResizeArray ()
                    else ResizeArray ()

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
                ti.contextValue <- Some "FAKE.targetsOutline.item"
                ti.tooltip <- Some node.Description

                let c = createEmpty<Command>
                c.command <- "FAKE.targetsOutline.goTo"
                c.title <- "open"
                c.arguments <- Some (ResizeArray [| unbox node|])
                ti.command <- Some c
                ti
        }

    module private ShowInActivity =
        let private setInFsharpActivity = Context.cachedSetter<bool> "fsharp.showCodeOutlineInFsharpActivity"
        let private setInExplorerActivity = Context.cachedSetter<bool> "fsharp.showCodeOutlineInExplorerActivity"

        let showInFsharpActivity () =
            let showIn = "FAKE.showTargetsOutlineIn" |> Configuration.get "explorer"
            showIn = "fsharp"

        let initializeAndGetId () =
            let inFsharpActivity = showInFsharpActivity ()
            setInFsharpActivity inFsharpActivity
            setInExplorerActivity (not inFsharpActivity)

            if inFsharpActivity then "ionide.fakeTargetsOutlineInActivity" else "ionide.fakeTargetsOutline"

    let setShowCodeOutline = Context.cachedSetter<bool> "fsharp.showCodeOutline"

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
            let line =
                let m = unbox<Model> n
                m.Declaration.Line

            let args =
                createObj [
                    "lineNumber" ==> line - 1
                    "at" ==> "center"
                ]

            vscode.commands.executeCommand("revealLine", args)
            |> unbox
        )) |> context.subscriptions.Add

        commands.registerCommand("ionide.fakeTargetsOutline.reloadTargets", Func<obj, obj>(fun _ ->
            refresh.fire undefined |> unbox
        )) |> context.subscriptions.Add

        let provider = createProvider ()
        let treeOptions = createEmpty<CreateTreeViewOptions<Model>>
        treeOptions.treeDataProvider <- provider
        treeOptions.showCollapseAll <- Some true
        let treeView = window.createTreeView(treeViewId, treeOptions)
        context.subscriptions.Add treeView

        //window.registerTreeDataProvider(treeViewId, createProvider () )
        //|> context.subscriptions.Add
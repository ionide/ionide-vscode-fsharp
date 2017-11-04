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
open Ionide.VSCode.Helpers

module CodeOutline =

    type Model =
        | TopLevelNamespace of name : string * entries : Model list
        | Type of name : string * typ : string * range : Range * entries : Model list
        | Function of name : string * typ : string * range : Range

    type NodeEntry = {
        Key : string
        Children : Dictionary<string, NodeEntry>
        Symbol : Symbol
    }

    let refresh = EventEmitter<Model> ()


    let private getIconPath light dark =
        let plugPath =
            try
                (VSCode.getPluginPath "Ionide.ionide-fsharp")
            with
            | _ ->  (VSCode.getPluginPath "Ionide.Ionide-fsharp")

        let p = createEmpty<TreeIconPath>
        p.dark <- Path.join(plugPath, "images", dark)
        p.light <- Path.join(plugPath, "images", light)
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

    let modelSortBy model =
        match model with
        | TopLevelNamespace _ ->  -1
        | Type (_,_, r, _) | Function (_,_,r) -> r.StartLine

    let rec toModel (entry : NodeEntry)  =
        if entry.Children.Count > 0 then
            let childs =
                entry.Children
                |> Seq.map (fun n -> toModel n.Value )
                |> Seq.toList
            Type(entry.Key, entry.Symbol.GlyphChar, entry.Symbol.Range, childs |> List.sortBy (modelSortBy))
        else
            Function(entry.Key, entry.Symbol.GlyphChar, entry.Symbol.Range)



    let map (input : Symbols[]) : Model =
        match input |> Seq.tryFind (fun n -> n.Declaration.IsTopLevel ) with
        | None -> TopLevelNamespace ("", [])
        | Some topLevel ->
            let entry = {Key = ""; Children = new Dictionary<_,_>(); Symbol = topLevel.Declaration}
            let symbols =
                input
                |> Seq.filter (fun n -> not n.Declaration.IsTopLevel)
                |> Seq.collect (fun n -> Array.append (Array.singleton n.Declaration) (n.Nested |> Array.map (fun x -> {x with Name = n.Declaration.Name + "." + x.Name }))  )
            let symbols = Seq.append (topLevel.Nested) symbols
            symbols |> Seq.iter (fun x -> add' entry x 0 |> ignore )

            let x =
                entry.Children
                |> Seq.map (fun n -> toModel n.Value )
                |> Seq.toList
                |> List.sortBy (modelSortBy)


            TopLevelNamespace (topLevel.Declaration.Name, x)

    let private getSubmodel node =
        match node with
        | TopLevelNamespace (_,e) -> e
        | Type (_,_,_, e) -> e
        | _ -> []

    let private getLabel node =
        match node with
        | TopLevelNamespace (n,_) -> n
        | Type (n,_,_, _) -> n
        | Function(n,_,_) -> n

    let getRoot (doc : TextDocument) =
        promise {
            let text = doc.getText()
            let! o = LanguageService.declarations doc.fileName text (unbox doc.version)
            if isNotNull o then
                return o.Data |> map
            else
                return unbox null
        }

    let getIcon node =
        match node with
        | TopLevelNamespace _ -> Some <| getIconPath "icon-module-light.svg" "icon-module-dark.svg"
        | Type (_, t, _, _) | Function (_, t, _) ->
            match t with
            | "C" | "E" | "T" | "I" -> Some <|  getIconPath "icon-class-light.svg" "icon-class-dark.svg"
            | "N" -> Some <| getIconPath "icon-module-light.svg" "icon-module-dark.svg"
            | "S" | "P" | "M" -> Some <| getIconPath "icon-property-light.svg" "icon-property-dark.svg"
            | "F" | "Fc"  -> Some <| getIconPath "icon-function-light.svg" "icon-function-dark.svg"
            | _ -> None

    let createProvider () : TreeDataProvider<Model> =
        { new TreeDataProvider<Model>
          with
            member this.onDidChangeTreeData =
                refresh.event

            member this.getChildren(node) =
                if JS.isDefined node then
                    getSubmodel node |> ResizeArray
                else
                    let doc = window.activeTextEditor
                    if JS.isDefined doc
                    then
                        match doc.document with
                        | Document.FSharp ->
                            promise {
                                let! x = getRoot doc.document
                                return if isNotNull x then x |> List.singleton |> ResizeArray else unbox (ResizeArray ())
                            } |> unbox
                        | _ ->  ResizeArray ()
                    else ResizeArray ()

            member this.getTreeItem(node) =
                let ti = createEmpty<TreeItem>
                ti.label <- getLabel node
                ti.collapsibleState <-
                    match node with
                    | TopLevelNamespace _ -> Some TreeItemCollapsibleState.Expanded
                    | Type (_, typ, _, _) ->
                        match typ with
                        | "N" -> Some TreeItemCollapsibleState.Expanded
                        | _ -> Some TreeItemCollapsibleState.Collapsed
                    | _ -> None
                ti.iconPath <- getIcon node
                ti.contextValue <- Some "fsharp.codeOutline.item"

                let c = createEmpty<Command>
                c.command <- "fsharp.codeOutline.goTo"
                c.title <- "open"
                c.arguments <- Some (ResizeArray [| unbox node|])
                ti.command <- Some c


                ti


        }

    let activate (context: ExtensionContext) =
        commands.registerCommand("fsharp.codeOutline.goTo", Func<obj, obj>(fun n ->
            let line =
                match unbox<Model> n with
                | TopLevelNamespace _ -> 1
                | Type (_,_, r, _) | Function (_,_, r) -> r.StartLine



            let args =
                createObj [
                    "lineNumber" ==> line - 1
                    "at" ==> "center"
                ]

            vscode.commands.executeCommand("revealLine", args)
            |> unbox
        )) |> context.subscriptions.Add

        commands.registerCommand("ionide.codeOutline.collapseAll", Func<obj, obj>(fun _ ->
            refresh.fire undefined |> unbox
        )) |> context.subscriptions.Add

        window.registerTreeDataProvider("ionide.codeOutline", createProvider () )
        |> context.subscriptions.Add

namespace Ionide.VSCode.FSharp

open System
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open Fable.Core

[<AutoOpen>]
module ContextExtensions =
    type ExtensionContext with

        member inline x.Subscribe< ^t when ^t: (member dispose: unit -> obj option)>(item: ^t) =
            x.subscriptions.Add(unbox (box item))

[<AutoOpen>]
module PromiseBuilder =
    type Foo = JS.PromiseConstructor

    type Promise.PromiseBuilder with

        member x.Source(p: Thenable<'t>) : JS.Promise<'t> = box p |> unbox
        member x.Source(p: JS.Promise<'t>) : JS.Promise<'t> = p

[<AutoOpen>]
module Json =
    open Thoth.Json

    /// Previously `Fable.Core.JsInterop.ofJson`
    /// Instantiate F# objects from JSON
    let inline ofJson<'a> json : 'a =
        // see https://fable.io/blog/Migration-to-Fable2.html#breaking-changes
        Decode.Auto.unsafeFromString<'a> (json)

[<RequireQualifiedAccess>]
module CodeRange =
    /// Converts Range DTO to VS Code Range.
    let fromDTO (range: DTO.Range) =
        vscode.Range.Create(
            float range.StartLine - 1.,
            float range.StartColumn - 1.,
            float range.EndLine - 1.,
            float range.EndColumn - 1.
        )

    let fromHighlightingDTO (range: DTO.Range) =
        vscode.Range.Create(
            float range.StartLine - 1.,
            float range.StartColumn,
            float range.EndLine - 1.,
            float range.EndColumn
        )

    let fromSimplifiedNameRange (range: DTO.Range) =
        vscode.Range.Create(
            float range.StartLine - 1.,
            float range.StartColumn - 2.,
            float range.EndLine - 1.,
            float range.EndColumn - 2.
        )

    /// Converts Declaration DTO of a specific `length` to VS Code Range.
    let fromDeclaration (decl: DTO.Declaration) (length: float) =
        vscode.Range.Create(
            float decl.Line - 1.,
            float decl.Column - 1.,
            float decl.Line - 1.,
            float decl.Column + length - 1.
        )

    /// Converts SymbolUse DTO to VS Code Range.
    let fromSymbolUse (su: DTO.SymbolUse) =
        vscode.Range.Create(
            float su.StartLine - 1.,
            float su.StartColumn - 1.,
            float su.EndLine - 1.,
            float su.EndColumn - 1.
        )

    let fromError (error: DTO.Error) =
        vscode.Range.Create(
            float error.StartLine - 1.,
            float error.StartColumn - 1.,
            float error.EndLine - 1.,
            float error.EndColumn - 1.
        )

[<RequireQualifiedAccess>]
module String =

    let trim (s: string) = s.Trim()

    let replace (oldVal: string) (newVal: string) (str: string) : string =
        match str with
        | null -> null
        | _ -> str.Replace(oldVal, newVal)

    let split separator (s: string) = s.Split separator

    let endWith ending (s: string) = s.EndsWith ending

    let startWith ending (s: string) = s.StartsWith ending

    let quote (s: string) =
        let isQuoted (s: string) = s.StartsWith @"""" && s.EndsWith @""""
        let containsWhitespace = Seq.exists (fun c -> c = ' ' || c = '\t' || c = '\n')
        let quote = sprintf @"""%s"""

        match s with
        | s when s |> isQuoted |> not && s |> containsWhitespace -> quote s
        | s -> s

[<RequireQualifiedAccess>]
module Option =

    let fill (def: 'a) (x: 'a option) : 'a =
        match x with
        | Some x -> x
        | None -> def

[<RequireQualifiedAccess>]
module Document =
    let (|FSharp|CSharp|VB|FSharpScript|Markdown|Other|) (document: TextDocument) =
        if document.languageId = "fsharp" && document.fileName.EndsWith "fsx" then
            FSharpScript
        else if document.languageId = "fsharp" then
            FSharp
        else if document.languageId = "csharp" then
            CSharp
        else if document.languageId = "vb" then
            VB
        else if document.languageId = "markdown" then
            Markdown
        else
            Other

[<RequireQualifiedAccess>]
module Configuration =

    let tryGet key =
        let configuredValue = workspace.getConfiguration().get (key)

        if configuredValue = Some "" then None else configuredValue

    /// get a key of a given value, asusming that a default has been set by extension configuration settings
    let getUnsafe key =
        workspace.getConfiguration().get(key).Value

    let get defaultValue key =
        workspace.getConfiguration().get (key, defaultValue)

    let getInContext context defaultValue key =
        workspace.getConfiguration(scope = U4.Case1 context).get (key, defaultValue)

    /// write the value to the given key in the workspace configuration
    let set key value =
        workspace
            .getConfiguration()
            .update (key, value, configurationTarget = U2.Case1 ConfigurationTarget.Workspace)

    /// write the value to the given key in the global configuration
    let setGlobal key value =
        workspace
            .getConfiguration()
            .update (key, value, configurationTarget = U2.Case1 ConfigurationTarget.Global)

[<AutoOpen>]
module Utils =
    open Fable.Core

    let isNotNull o = o |> unbox <> null

    [<Emit("$0 === undefined")>]
    let isUndefined (x: 'a) : bool = jsNative

    type System.Collections.Generic.Dictionary<'key, 'value> with

        [<Emit("$0.has($1) ? $0.get($1) : null")>]
        member this.TryGet(key: 'key) : 'value option = jsNative

module Message =
    let choice title =
        let m = Fable.Core.JsInterop.createEmpty<MessageItem>
        m.title <- title
        m

[<AutoOpen>]
module JS =

    /// Schedules execution of a one-time callback after delay milliseconds.
    /// Returns a Timeout for use with `clearTimeout`.
    [<Emit("setTimeout($0, $1)")>]
    let setTimeout (callback: unit -> unit) (delay: float) : Node.Base.Timer = jsNative

    /// Cancels a Timeout object created by `setTimeout`.
    [<Emit("clearTimeout($0)")>]
    let clearTimeout (timeout: Node.Base.Timer) : unit = jsNative

    [<Emit("debugger")>]
    let debugger () : unit = failwith "JS Only"

    type JsObject =
        [<Emit("$0[$1]")>]
        member __.get<'a>(key: string) : 'a = jsNative

        [<Emit("$0.hasOwnProperty($1)?$0[$1]:null")>]
        member __.tryGet<'a>(key: string) : Option<'a> = jsNative

        [<Emit("$0.hasOwnProperty($1)")>]
        member __.hasOwnProperty(key: string) : bool = jsNative

        [<Emit("$0[$1]=$2")>]
        member __.set<'a>(key: string, value: 'a) = jsNative

        [<Emit("$0[$1]=$2")>]
        member __.set<'a>(key: string, value: 'a option) = jsNative

        [<Emit("delete $0[$1]")>]
        member __.delete(key: string) : unit = jsNative

        [<Emit("{}")>]
        static member empty: JsObject = jsNative

    type JsObjectAsDictionary<'a> =
        [<Emit("$0[$1]")>]
        member __.get(key: string) : 'a = jsNative

        [<Emit("$0.hasOwnProperty($1)?$0[$1]:null")>]
        member __.tryGet(key: string) : Option<'a> = jsNative

        [<Emit("$0.hasOwnProperty($1)")>]
        member __.hasOwnProperty(key: string) : bool = jsNative

        [<Emit("$0[$1]=$2")>]
        member __.set(key: string, value: 'a) = jsNative

        [<Emit("$0[$1]=$2")>]
        member __.set(key: string, value: 'a option) = jsNative

        [<Emit("delete $0[$1]")>]
        member __.delete(key: string) : unit = jsNative

        [<Emit("{}")>]
        static member empty: JsObjectAsDictionary<'a> = jsNative

    let inline undefined<'a> = unbox<'a> ()

[<AutoOpen>]
module Patterns =

    let (|StartsWith|_|) (pat: string) (str: string) =
        match str with
        | null -> None
        | _ when str.StartsWith pat -> Some str
        | _ -> None

    let (|Contains|_|) (pat: string) (str: string) =
        match str with
        | null -> None
        | _ when str.Contains pat -> Some str
        | _ -> None

    let inline private title item =
        (^t: (member get_title: unit -> string) (item))

    let inline (|HasTitle|_|) expected t =
        if title t = expected then Some() else None

[<RequireQualifiedAccess>]
module Array =

    let splitAt (n: int) (xs: 'a[]) : 'a[] * 'a[] =
        match xs with
        | [||]
        | [| _ |] -> xs, [||]
        | _ when n >= xs.Length || n < 0 -> xs, [||]
        | _ -> xs.[0 .. n - 1], xs.[n..]

module Promise =
    open Fable.Core

    // source: https://github.com/ionide/ionide-vscode-helpers/blob/5e4c28c79ed565497cd481fac2f22ee2d8d28406/src/Helpers.fs#L98
    let empty<'T> = Promise.lift (unbox<'T> null)

    let ofThenable (t: Thenable<'t>) : JS.Promise<'t> = unbox (box t)
    let toThenable (p: JS.Promise<'t>) : Thenable<'t> = unbox (box p)

    [<Emit("typeof ($0 || {}).then === 'function'")>]
    let isThenable (x: obj) : bool = jsNative

    let ofMaybeThenable (ifValue: 'T1 -> 'T2) (t: U2<'T1, Thenable<'T2>>) : JS.Promise<'T2> =
        match box t with
        | t when isThenable t -> unbox t |> ofThenable
        | t -> unbox t |> ifValue |> Promise.lift

    let onSuccess = Promise.tap

    // source: https://github.com/ionide/ionide-vscode-helpers/blob/5e4c28c79ed565497cd481fac2f22ee2d8d28406/src/Helpers.fs#L92
    let onFail a (pr: JS.Promise<_>) : JS.Promise<_> =
        pr
        |> Promise.catchBind (fun reason ->
            a reason |> ignore
            Promise.reject reason)

    let suppress (pr: JS.Promise<'T>) =
        pr |> Promise.either (fun _ -> U2.Case1()) (fun _ -> U2.Case1())

    let executeForAll f items =
        match items with
        | [] -> empty
        | [ x ] -> f x
        | x :: tail -> tail |> List.fold (fun acc next -> acc |> Promise.bind (fun _ -> f next)) (f x)

    let executeWithMaxParallel maxParallelCount (f: 'a -> JS.Promise<'b>) (items: 'a list) =
        let initial = items |> List.take maxParallelCount

        let mutable remaining =
            Collections.Generic.Queue(collection = (items |> List.skip maxParallelCount))

        let rec startNext promise =
            promise
            |> Promise.bind (fun _ ->
                if remaining.Count = 0 then
                    promise
                else
                    let next: 'a = remaining.Dequeue()
                    startNext (f next))

        initial |> List.map (f >> startNext) |> Promise.all

module Event =

    let invoke (listener: 'T -> _) (event: Fable.Import.VSCode.Vscode.Event<'T>) = event.Invoke(listener >> unbox)

module Context =

    open Fable.Import

    let set<'a> (name: string) (value: 'a) =
        commands.executeCommand ("setContext", Some(box name), Some(box value))
        |> ignore

    let cachedSetter<'a when 'a: equality> (name: string) =
        let mutable current: 'a option = None

        fun (value: 'a) ->
            if current <> Some value then
                set name value
                current <- Some value

open Fable.Import
open Fable.Core
open Ionide.VSCode.Helpers

[<AllowNullLiteral>]
type ShowStatus private (panel: WebviewPanel, body: string) as this =
    let renderPage (body: string) =
        sprintf
            """<!DOCTYPE html>
<html lang="en">
<head>
</head>
<body>
%s
</body>
</html>"""
            body

    let mutable _disposables: ResizeArray<Disposable> = ResizeArray<Disposable>()

    static let mutable instance: ShowStatus = null

    do
        panel.onDidDispose.Invoke(
            (fun () ->
                this.Dispose()
                None),
            this,
            _disposables
        )
        |> ignore

        panel.onDidChangeViewState.Invoke(
            (fun _ev ->
                if panel.visible then
                    this.Update()

                None),
            this,
            _disposables
        )
        |> ignore

        this.Update()

    static member ViewType = "project_status"

    static member CreateOrShow(projectPath: string, projectName: string) =
        let title = sprintf "Project %s status" projectName

        let panel =
            window.createWebviewPanel (ShowStatus.ViewType, title, U2.Case1 ViewColumn.One)

        promise {
            let uri =
                vscode.Uri.parse (
                    sprintf "fsharp-workspace:projects/status?path=%s" (JS.encodeURIComponent (projectPath))
                )

            let! (doc: TextDocument) = workspace.openTextDocument uri
            return doc.getText ()
        }
        |> Promise.onSuccess (fun bodyStr ->
            printfn "%s" bodyStr
            instance <- ShowStatus(panel, bodyStr))
        |> Promise.onFail (fun err ->
            JS.console.error ("ShowStatus.CreateOrShow failed:\n", err)

            window.showErrorMessage ("We couldn't generate the status report", [||])
            |> ignore)
        |> ignore

    member __.Dispose() =
        instance <- null
        panel.dispose () |> ignore

        for disposable in _disposables do
            if isNotNull disposable then
                disposable.dispose () |> ignore

        _disposables <- ResizeArray<Disposable>()

    member __.Update() = panel.webview.html <- renderPage body

[<RequireQualifiedAccess>]
module VSCodeExtension =

    let private extensionName =
#if IONIDE_EXPERIMENTAL
        "ionide-fsharp-experimental"
#else
        "ionide-fsharp"
#endif

    let ionidePluginPath () =

        let capitalize (s: string) =
            sprintf "%c%s" (s.[0] |> Char.ToUpper) (s.Substring(1))

        let oldExtensionName = capitalize extensionName

        let path =
            try
                (VSCode.getPluginPath (sprintf "Ionide.%s" extensionName))
            with _ ->
                (VSCode.getPluginPath (sprintf "Ionide.%s" oldExtensionName))

        match path with
        | Some p -> p
        | None -> failwith "couldn't resolve plugin path"

    let workbenchViewId () =
        sprintf "workbench.view.extension.%s" extensionName

[<AutoOpen>]
module Objectify =
    let inline objfy2 (f: 'a -> 'b) : ResizeArray<obj option> -> obj option = unbox f
    let inline objfy3 (f: 'a -> 'b -> 'c) : ResizeArray<obj option> -> obj option = unbox f
    let inline objfy4 (f: 'a -> 'b -> 'c -> 'd) : ResizeArray<obj option> -> obj option = unbox f
    let inline objfy5 (f: 'a -> 'b -> 'c -> 'd -> 'e) : ResizeArray<obj option> -> obj option = unbox f

namespace Ionide.VSCode.FSharp

open System
open Fable.Import.vscode

[<RequireQualifiedAccess>]
module CodeRange =
    type CodeRange = Fable.Import.vscode.Range

    /// Converts Range DTO to VS Code Range.
    let fromDTO (range: DTO.Range) : CodeRange =
        CodeRange (float range.StartLine - 1.,
                   float range.StartColumn - 1.,
                   float range.EndLine - 1.,
                   float range.EndColumn - 1.)

    let fromSimplifiedNameRange (range: DTO.Range) : CodeRange =
        CodeRange (float range.StartLine - 1.,
                   float range.StartColumn - 2.,
                   float range.EndLine - 1.,
                   float range.EndColumn - 2.)


    /// Converts Declaration DTO of a specific `length` to VS Code Range.
    let fromDeclaration (decl: DTO.Declaration) (length: float) : CodeRange =
        CodeRange (float decl.Line - 1.,
                   float decl.Column - 1.,
                   float decl.Line - 1.,
                   float decl.Column + length - 1.)

    /// Converts SymbolUse DTO to VS Code Range.
    let fromSymbolUse (su: DTO.SymbolUse) : CodeRange =
        CodeRange (float su.StartLine - 1.,
                   float su.StartColumn - 1.,
                   float su.EndLine - 1.,
                   float su.EndColumn - 1.)

    let fromError (error : DTO.Error) : CodeRange =
        CodeRange (float error.StartLine - 1.,
                   float error.StartColumn - 1.,
                   float error.EndLine - 1.,
                   float error.EndColumn - 1.)

[<RequireQualifiedAccess>]
module String =
    let trim (s: string) = s.Trim()
    let replace (oldVal: string) (newVal: string) (str: string) : string =
        match str with
        | null -> null
        | _ -> str.Replace (oldVal, newVal)
    let split seperator (s : string) = s.Split seperator

    let endWith ending (s : string) = s.EndsWith ending

    let startWith ending (s : string) = s.StartsWith ending


[<RequireQualifiedAccess>]
module Option =
    let fill (def: 'a) (x: 'a option) : 'a =
        match x with
        | Some x -> x
        | None -> def

[<RequireQualifiedAccess>]
module Document =
    let (|FSharp|CSharp|VB|Other|) (document : TextDocument) =
        if document.languageId = "fsharp" then FSharp
        else if document.languageId = "csharp" then CSharp
        else if document.languageId = "vb" then VB
        else Other

[<RequireQualifiedAccess>]
module Configuration =
    let get defaultValue key =
        workspace.getConfiguration().get(key, defaultValue)

[<AutoOpen>]
module Utils =
    open Fable.Core

    let isNotNull o = o |> unbox <> null

    type System.Collections.Generic.Dictionary<'key, 'value> with
        [<Emit("$0.has($1) ? $0.get($1) : null")>]
        member this.TryGet(key: 'key): 'value option = jsNative

[<AutoOpen>]
module JS =
    open Fable.Core
    open Fable.Import.Node

    /// Schedules execution of a one-time callback after delay milliseconds.
    /// Returns a Timeout for use with `clearTimeout`.
    [<Emit("setTimeout($0, $1)")>]
    let setTimeout (callback: unit -> unit) (delay: float): Base.NodeJS.Timer = jsNative

    /// Cancels a Timeout object created by `setTimeout`.
    [<Emit("clearTimeout($0)")>]
    let clearTimeout (timeout: Base.NodeJS.Timer): unit = jsNative

    [<Emit("debugger")>]
    let debugger () : unit = failwith "JS Only"

    type JsObject =
        [<Emit("$0[$1]")>]
        member __.get<'a>(key: string): 'a = jsNative

        [<Emit("$0.hasOwnProperty($1)?$0[$1]:null")>]
        member __.tryGet<'a>(key: string): Option<'a> = jsNative

        [<Emit("$0.hasOwnProperty($1)")>]
        member __.hasOwnProperty(key: string): bool = jsNative

        [<Emit("$0[$1]=$2")>]
        member __.set<'a>(key: string, value: 'a) = jsNative

        [<Emit("$0[$1]=$2")>]
        member __.set<'a>(key: string, value: 'a option) = jsNative

        [<Emit("delete $0[$1]")>]
        member __.delete(key: string): unit = jsNative

        [<Emit("{}")>]
        static member empty: JsObject = jsNative

    type JsObjectAsDictionary<'a> =
        [<Emit("$0[$1]")>]
        member __.get(key: string): 'a = jsNative

        [<Emit("$0.hasOwnProperty($1)?$0[$1]:null")>]
        member __.tryGet(key: string): Option<'a> = jsNative

        [<Emit("$0.hasOwnProperty($1)")>]
        member __.hasOwnProperty(key: string): bool = jsNative

        [<Emit("$0[$1]=$2")>]
        member __.set(key: string, value: 'a) = jsNative

        [<Emit("$0[$1]=$2")>]
        member __.set(key: string, value: 'a option) = jsNative

        [<Emit("delete $0[$1]")>]
        member __.delete(key: string): unit = jsNative

        [<Emit("{}")>]
        static member empty: JsObjectAsDictionary<'a> = jsNative

    let inline undefined<'a> = unbox<'a> ()

[<AutoOpen>]
module Patterns =
    let (|StartsWith|_|) (pat: string) (str: string)  =
        match str with
        | null -> None
        | _ when str.StartsWith pat -> Some str
        | _ -> None

    let (|Contains|_|) (pat: string) (str: string)  =
        match str with
        | null -> None
        | _ when str.Contains pat -> Some str
        | _ -> None

[<RequireQualifiedAccess>]
module Array =
    let splitAt (n: int) (xs: 'a[]) : 'a[] * 'a[] =
        match xs with
        | [||] | [|_|] -> xs, [||]
        | _ when n >= xs.Length || n < 0 -> xs, [||]
        | _ -> xs.[0..n-1], xs.[n..]

module Markdown =
    open System.Text.RegularExpressions

    let private stringReplacePatterns =
        [ "&lt;", "<"
          "&gt;", ">"
          "&quot;", "\""
          "&apos;", "'"
          "&amp;", "&"
          "<summary>", ""
          "</summary>", ""
          "<para>", ""
          "</para>", ""
          "<remarks>", ""
          "</remarks>", "" ]

    let private regexReplacePatterns =
        let r pat = Regex(pat, RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
        let code = Array.item 0 >> sprintf "`%s`"
        let returns = Array.item 0 >> sprintf "returns %s"
        let param (s: string[]) = sprintf "%s: %s"(s.[0].Substring(1, s.[0].Length - 2)) s.[1]
        [ r"<c>((?:(?!<c>)(?!<\/c>)[\s\S])*)<\/c>", code
          r"""<see\s+cref=(?:'[^']*'|"[^"]*")>((?:(?!<\/see>)[\s\S])*)<\/see>""", code
          r"""<param\s+name=('[^']*'|"[^"]*")>((?:(?!<\/param>)[\s\S])*)<\/param>""", param
          r"""<typeparam\s+name=('[^']*'|"[^"]*")>((?:(?!<\/typeparam>)[\s\S])*)<\/typeparam>""", param
          r"""<exception\s+cref=('[^']*'|"[^"]*")>((?:(?!<\/exception>)[\s\S])*)<\/exception>""", param
          r"""<a\s+href=('[^']*'|"[^"]*")>((?:(?!<\/a>)[\s\S])*)<\/a>""", fun s -> (s.[0].Substring(1, s.[0].Length - 2))

          r"<returns>((?:(?!<\/returns>)[\s\S])*)<\/returns>", returns
        ]

    /// Replaces XML tags with Markdown equivalents.
    /// List of standard tags: https://docs.microsoft.com/en-us/dotnet/fsharp/language-reference/xml-documentation
    let private replaceXml (str: string) : string =

        let res =
            regexReplacePatterns
            |> List.fold (fun res (regex: Regex, formatter: string[] -> string) ->
                // repeat replacing with same pattern to handle nested tags, like `<c>..<c>..</c>..</c>`
                let rec loop res : string =
                    match regex.Match res with
                    | m when m.Success ->
                        let [| firstGroup |], otherGroups =
                            m.Groups
                            |> Seq.cast<Group>
                            |> Seq.map (fun g -> g.Value)
                            |> Seq.toArray
                            |> Array.splitAt 1
                        loop <| res.Replace(firstGroup, formatter otherGroups)
                    | _ -> res
                loop res
            ) str

        stringReplacePatterns
        |> List.fold (fun (res: string) (oldValue, newValue) ->
            res.Replace(oldValue, newValue)
        ) res

    let createCommentBlock (comment: string) : MarkdownString =
        comment
        |> replaceXml
        |> String.split [|'\n'|]
        |> Array.filter (not << String.IsNullOrWhiteSpace)
        |> Array.map String.trim
        |> String.concat "\n\n"
        |> (fun v -> MarkdownString v)

module Promise =
    open Fable.Import.JS
    open Ionide.VSCode.Helpers

    let suppress (pr:Promise<'T>) =
        pr |> Ionide.VSCode.Helpers.Promise.catch (fun _ -> promise { () })

    let executeForAll f items =
        match items with
        | [] -> Ionide.VSCode.Helpers.Promise.lift (null |> unbox)
        | [x] -> f x
        | x::tail ->
            tail |> List.fold (fun acc next -> acc |> Ionide.VSCode.Helpers.Promise.bind (fun _ -> f next)) (f x)

module Event =

    let invoke (listener: 'T -> _) (event: Fable.Import.vscode.Event<'T>) =
        event.Invoke(unbox<System.Func<_, _>>(fun a -> listener a))

module Context =
    open Fable.Import

    let set<'a> (name: string) (value: 'a) =
        vscode.commands.executeCommand("setContext", name, value) |> ignore

    let cachedSetter<'a when 'a : equality> (name: string) =
        let mutable current: 'a option = None
        fun (value:'a) ->
            if current <> Some value then
                set name value
                current <- Some value
namespace Ionide.VSCode.FSharp

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
    let (|FSharp|Other|) (document : TextDocument) =
        if document.languageId = "fsharp" then FSharp else Other

[<RequireQualifiedAccess>]
module Configuration =
    let get defaultValue key =
        workspace.getConfiguration().get(key, defaultValue)

[<AutoOpen>]
module Utils =
    let isNotNull o = o |> unbox <> null

[<AutoOpen>]
module JS =
    open Fable.Core

    [<Emit("setTimeout($0,$1)")>]
    let setTimeout(cb, delay) : obj = failwith "JS Only"

    [<Emit("clearTimeout($0)")>]
    let clearTimeout(timer) : unit = failwith "JS Only"

    [<Emit("debugger")>]
    let debugger () : unit = failwith "JS Only"

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
    let private replacePatterns =
        let r pat = Regex(pat, RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
        [ r"<c>(((?!<c>)(?!<\/c>).)*)<\/c>", sprintf "`%s`" ]

    let private removePatterns =
        [ "<summary>"; "</summary>"; "<para>"; "</para>" ]

    /// Replaces XML tags with Markdown equivalents.
    let replaceXml (str: string) : string =
        let res =
            replacePatterns
            |> List.fold (fun res (regex: Regex, formatter: string -> string) ->
                // repeat replacing with same pattern to handle nested tags, like `<c>..<c>..</c>..</c>`
                let rec loop res : string =
                    match regex.Match res with
                    | m when m.Success -> loop <| res.Replace(m.Groups.[0].Value, formatter (m.Groups.[1].Value))
                    | _ -> res
                loop res
            ) str

        removePatterns
        |> List.fold (fun (res: string) pat ->
             res.Replace(pat, "")
        ) res

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

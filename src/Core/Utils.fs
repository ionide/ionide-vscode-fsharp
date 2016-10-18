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
    
    let chainExecution first next items =
        match items with
        | [] -> ()
        | [x] -> first x |> ignore
        | x::tail -> tail |> List.fold (fun acc y -> next acc y) (first x) |> ignore
    


[<AutoOpen>]
module JS =
    open Fable.Core

    [<Emit("setTimeout($0,$1)")>]
    let setTimeout(cb, delay) : obj = failwith "JS Only"

    [<Emit("clearTimeout($0)")>]
    let clearTimeout(timer) : unit = failwith "JS Only"

    [<Emit("debugger")>]
    let debugger () : unit = failwith "JS Only"


module Promise =
    open Fable.Import.JS
    open Ionide.VSCode.Helpers

    let suppress (pr:Promise<'T>) =
        pr |> Ionide.VSCode.Helpers.Promise.catch (fun _ -> promise { () })
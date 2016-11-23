namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers

module WorkspaceSymbols =
    let private createProvider () =

        let convertToKind code =
            match code with
            | "C" -> SymbolKind.Class     (*  CompletionItemKind.Class      *)
            | "E" -> SymbolKind.Enum      (*  CompletionItemKind.Enum       *)
            | "S" -> SymbolKind.Property      (*  CompletionItemKind.Value      *)
            | "I" -> SymbolKind.Interface     (*  CompletionItemKind.Interface  *)
            | "N" -> SymbolKind.Module      (*  CompletionItemKind.Module     *)
            | "M" -> SymbolKind.Method     (*  CompletionItemKind.Method     *)
            | "P" -> SymbolKind.Property      (*  CompletionItemKind.Property   *)
            | "F" -> SymbolKind.Variable     (*  CompletionItemKind.Field      *)
            | "T" -> SymbolKind.Class      (*  CompletionItemKind.Class      *)
            | _   -> 0 |> unbox

        let relative f =
            path.relative (workspace.rootPath, f)

        let mapRes o =
             if isNotNull o then
                o.Data |> Array.map (fun syms ->
                    let oc = createEmpty<SymbolInformation>
                    oc.name <- syms.Declaration.Name
                    oc.kind <- syms.Declaration.GlyphChar |> convertToKind
                    oc.containerName <- relative syms.Declaration.File
                    let loc = createEmpty<Location>
                    loc.range <- CodeRange.fromDTO syms.Declaration.BodyRange
                    loc.uri <- Uri.file syms.Declaration.File
                    oc.location <- loc
                    let ocs =  syms.Nested |> Array.map (fun sym ->
                        let oc = createEmpty<SymbolInformation>
                        oc.name <- sym.Name
                        oc.kind <- sym.GlyphChar |> convertToKind
                        oc.containerName <- relative sym.File
                        let loc = createEmpty<Location>
                        loc.range <- CodeRange.fromDTO sym.BodyRange
                        loc.uri <- Uri.file sym.File
                        oc.location <- loc
                        oc )
                    ocs |> Array.append (Array.create 1 oc)) |> Array.concat
                else
                    [||]

        { new WorkspaceSymbolProvider
          with
            member this.provideWorkspaceSymbols(q, ct) =
                promise {
                    let! o = LanguageService.declarationsProjects ()
                    return mapRes o |> ResizeArray
                } |> Case2
        }

    let activate selector (disposables: Disposable[]) =
        languages.registerWorkspaceSymbolProvider(createProvider())
        |> ignore
        ()

module Ionide.VSCode.FSharp.PipelineHints

open System.Collections.Generic
open Fable.Core
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open Fable.Core.JsInterop
open DTO
open LineLensShared

type Number = float

let private logger =
    ConsoleAndOutputChannelLogger(Some "PipelineHints", Level.DEBUG, None, Some Level.DEBUG)

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module PipelineHintsConfig =
    let defaultConfig = { enabled = false; prefix = " //  " }

    let getConfig () =
        let cfg = workspace.getConfiguration ()

        {
          // we can only enable the feature overall if it's explicitly enabled and
          // inline values are disabled (because inline values deliver the same functionality)
          enabled =
            cfg.get ("FSharp.pipelineHints.enabled", defaultConfig.enabled)
            && not (cfg.get ("FSharp.inlineValues.enabled", false))
          prefix = cfg.get ("FSharp.pipelineHints.prefix", defaultConfig.prefix) }


module PipelineDecorationUpdate =

    let interestingSymbolPositions
        (doc: TextDocument)
        (lines: PipelineHint[])
        : (Vscode.Range * string[] * Vscode.Range option)[] =
        lines
        |> Array.map (fun n ->
            let textLine = doc.lineAt (float n.Line)

            let previousTextLine =
                n.PrecedingNonPipeExprLine |> Option.map (fun l -> (doc.lineAt (float l)).range)

            textLine.range, n.Types, previousTextLine)

    /// match something like " 'T1 is int list "
    let typeParamRegex = JS.Constructors.RegExp.Create @"'.+?\s.+?\s([\s\S]+)"

    let private getSignature (index: int) (range: Vscode.Range, tts: string[]) =
        let tt = tts.[index]
        let groups = typeParamRegex.Match(tt).Groups
        let res = groups[1].Value
        range, "  " + res

    let private getSignatures (range: Vscode.Range, tts: string[], previousNonPipeLine: Vscode.Range option) =
        match previousNonPipeLine with
        | Some previousLine -> [| getSignature 0 (previousLine, tts); getSignature 1 (range, tts) |]
        | None -> [| getSignature 1 (range, tts) |]


    let declarationsResultToSignatures (doc: TextDocument) (declarationsResult: DTO.PipelineHintsResult) uri =
        promise {
            let interesting = declarationsResult.Data |> interestingSymbolPositions doc

            let signatures = interesting |> Array.collect (getSignatures)
            return signatures
        }

    let signatureToDecoration (config: LineLensShared.LineLensConfig) doc (r, s) =
        LineLensShared.LineLensDecorations.create "fsharp.pipelineHints" r (config.prefix + s)

let private pipelineHintsDecorationUpdate: LineLensShared.DecorationUpdate =
    DecorationUpdate.updateDecorationsForDocument
        LanguageService.pipelineHints
        PipelineDecorationUpdate.declarationsResultToSignatures
        PipelineDecorationUpdate.signatureToDecoration



let createPipeLineHints () =
    LineLensShared.LineLens("PipelineHints", pipelineHintsDecorationUpdate, PipelineHintsConfig.getConfig)

let Instance = createPipeLineHints ()

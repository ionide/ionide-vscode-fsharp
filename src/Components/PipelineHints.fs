module Ionide.VSCode.FSharp.PipelineHints

open System.Collections.Generic
open Fable.Core
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open Fable.Core.JsInterop
open DTO
open LineLens2

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

    let private getSignature (index: int) (range: Vscode.Range, tts: string[]) =
        let tt = tts.[index]
        let id = tt.IndexOf("is")
        let res = tt.Substring(id + 3)
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

    let signatureToDecoration (config: LineLens2.LineLensConfig) doc (r, s) =
        LineLens2.LineLensDecorations.create "fsharp.pipelineHints" r (config.prefix + s)

let private pipelineHintsDecorationUpdate: LineLens2.DecorationUpdate =
    DecorationUpdate.updateDecorationsForDocument
        LanguageService.pipelineHints
        PipelineDecorationUpdate.declarationsResultToSignatures
        PipelineDecorationUpdate.signatureToDecoration



let createPipeLineHints () =
    LineLens2.LineLens(LineLensDecorations.decorationType, pipelineHintsDecorationUpdate, PipelineHintsConfig.getConfig)

let Instance = createPipeLineHints ()

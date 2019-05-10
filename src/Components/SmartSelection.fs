namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Ionide.VSCode.Helpers
open DTO

module SmartSelection =


    let rec mkSelectionRanges (ranges: Range[]) =
        printfn "mkRanges: %A" ranges
        if ranges.Length = 0 then
            None
        else
            let xs = ranges.[1..]
            let r = ranges.[0]
            let parent = mkSelectionRanges xs
            match parent with
            | None -> Some <| SelectionRange(CodeRange.fromDTO r)
            | Some s -> Some <| SelectionRange(CodeRange.fromDTO r, s)

    let createProvider() =
        { new SelectionRangeProvider with
            member this.provideSelectionRanges(document: TextDocument, positions: ResizeArray<Position>, token: CancellationToken): U2<ResizeArray<SelectionRange>,JS.Promise<ResizeArray<SelectionRange>>> =
                promise {
                    let poss = positions |> Seq.map (fun n -> {Line = int n.line + 1; Column = int n.character + 1}) |> Seq.toArray
                    let! res = LanguageService.rangesAtPosition document.fileName poss
                    let lst =
                        res.Data.Ranges
                        |> List.choose (List.toArray >> mkSelectionRanges)
                        |> ResizeArray
                    printf "TEST: %A" lst

                    return lst
                }
                |> unbox
        }


    let activate selector (context : ExtensionContext) =
        languages.registerSelectionRangeProvider(selector, createProvider())
        |> context.subscriptions.Add
        ()
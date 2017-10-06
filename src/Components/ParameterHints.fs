namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node

open DTO
open Ionide.VSCode.Helpers


module ParameterHints =
    let private createProvider () =

        let mapResult o =
            let res = SignatureHelp ()
            if isNotNull o then
                let sigs = o.Data.Overloads |> Array.choose (fun c ->
                    try
                        let tip = c.Tip.[0].[0]
                        let signature = SignatureInformation (tip.Signature, U2.Case1 tip.Comment)
                        c.Parameters |> Array.iter (fun p ->
                            let parameter = ParameterInformation (p.Name, U2.Case1 p.CanonicalTypeTextForSorting)
                            signature.parameters.Add (parameter )
                            |> ignore
                        )
                        Some signature
                    with
                    | e -> None) |> ResizeArray
                res.activeParameter <- float (o.Data.CurrentParameter)
                res.activeSignature <-
                    sigs
                    |> Seq.sortBy (fun n -> n.parameters.Count)
                    |> Seq.findIndex (fun s -> s.parameters.Count >= o.Data.CurrentParameter )
                    |> (+) 1
                    |> float
                res.signatures <- sigs
            res

        { new SignatureHelpProvider
          with
            member this.provideSignatureHelp(doc,pos, ct) =
                promise {
                   let! _ = LanguageService.parse doc.fileName (doc.getText ()) doc.version
                   let! res = LanguageService.methods (doc.fileName) (int pos.line + 1) (int pos.character + 1)
                   return mapResult res

                } |> U2.Case2 }

    let activate selector (context: ExtensionContext) =
        languages.registerSignatureHelpProvider(selector, createProvider(), "(", ",")
        |> context.subscriptions.Add

        ()

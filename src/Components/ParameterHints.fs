namespace Ionide.VSCode.FSharp

open Fable.Core
open Fable.Import.vscode

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
                        let signature = SignatureInformation (tip.Signature, U2.Case2 (tip.Comment |>  Markdown.createCommentBlock))
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
                    let sigs = sigs |> Seq.sortBy (fun n -> n.parameters.Count)
                    sigs
                    |> Seq.findIndex (fun s -> s.parameters.Count >= o.Data.CurrentParameter )
                    |> fun index -> if index + 1 >= (sigs |> Seq.length) then index else index + 1
                    |> float
                res.signatures <- sigs
            res

        { new SignatureHelpProvider
          with
            member __.provideSignatureHelp(doc,pos, ct) =
                promise {
                    let! _ = LanguageService.parse doc.fileName (doc.getText ()) doc.version
                    let! res = LanguageService.methods (doc.fileName) (int pos.line + 1) (int pos.character + 1)
                    return mapResult res

                } |> U2.Case2 }


    let activate selector (context : ExtensionContext) =
        languages.registerSignatureHelpProvider(selector, createProvider(), "(", ",")
        |> context.subscriptions.Add

        ()

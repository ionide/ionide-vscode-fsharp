namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.languages

open DTO
open Ionide.VSCode.Helpers

[<ReflectedDefinition>]
module ParameterHints =
    let private createProvider () =
        let provider = createEmpty<SignatureHelpProvider> ()

        let mapResult o = 
            let res = createEmpty<SignatureHelp> ()
            let sigs = o.Data.Overloads |> Array.map (fun c ->
                let signature = createEmpty<SignatureInformation> ()
                let tip = c.Tip.[0].[0]
                signature.label <-  tip.Signature
                signature.parameters <- [||]
                    //c.Parameters |> Array.map (fun p ->
                    //    let parameter = createEmpty<IParameter> ()
                    //    parameter.label <- p.Name
                    //    parameter )
                signature )
            res.activeParameter <- float (o.Data.CurrentParameter)
            res.activeSignature <- 0.
            res.signatures <- sigs
            res

        provider.``provideSignatureHelp <-`` (fun doc pos _ ->
            LanguageService.parse doc.fileName (doc.getText ())
            |> Promise.bind (fun _ -> LanguageService.methods (doc.fileName) (int pos.line + 1) (int pos.character + 1))
            |> Promise.success mapResult
            |> Promise.toThenable )
        provider

    let activate selector (disposables: Disposable[]) =
        Globals.registerSignatureHelpProviderOverload2(selector, createProvider(), [|"("|])
        |> ignore

        ()

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
                try
                    let tip = c.Tip.[0].[0]
                    let signature = SignatureInformation.Create (tip.Signature, tip.Comment)
                    c.Parameters |> Array.iter (fun p ->
                        let parameter = ParameterInformation.Create (p.Name, p.CanonicalTypeTextForSorting)
                        signature.parameters.pushOverload2(parameter )
                        |> ignore
                    )
                    Some signature
                with 
                | e -> 
                    Globals.console.error e
                    None) |> Array.choose id
            res.activeParameter <- float (o.Data.CurrentParameter)
            res.activeSignature <- 0.
            res.signatures <- sigs
            Globals.console.log res
            res

            
        let logError (o : obj) = 
            Globals.console.error o
            null |> unbox<SignatureHelp>

        provider.``provideSignatureHelp <-`` (fun doc pos _ ->
            LanguageService.parse doc.fileName (doc.getText ())
            |> Promise.bind (fun _ -> LanguageService.methods (doc.fileName) (int pos.line + 1) (int pos.character + 1))
            |> Promise.either mapResult logError
            |> Promise.toThenable )
        
        provider

    let activate selector (disposables: Disposable[]) =
        Globals.registerSignatureHelpProviderOverload2(selector, createProvider(), "(", ",")
        |> ignore

        ()

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
                    let signature = createEmpty<SignatureInformation> ()
                    let tip = c.Tip.[0].[0]
                    signature.label <-  tip.Signature
                    signature.documentation <- tip.Comment
                    signature.parameters <-
                        c.Parameters |> Array.map (fun p ->
                            let parameter = createEmpty<ParameterInformation> ()
                            parameter.label <- p.Name
                            parameter.documentation <- p.Description
                            parameter )
                    Some signature
                with 
                | e -> 
                    Globals.console.error e
                    None) |> Array.choose id
            res.activeParameter <- float (o.Data.CurrentParameter)
            res.activeSignature <- 0.
            res.signatures <- sigs
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

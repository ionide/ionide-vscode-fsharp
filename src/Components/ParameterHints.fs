namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.Modes

open DTO

[<ReflectedDefinition>]
module ParameterHints =


    let private createProvider () =
        let provider = createEmpty<IParameterHintsSupport> ()
        provider.triggerCharacters <- [|"("|]
        //provider.excludeTokens <- [||]
        provider.``getParameterHints <-`` (fun doc pos _ ->
            LanguageService.methods (doc.getPath ()) (int pos.line) (int pos.character)
            |> Promise.success (fun o ->
                let res = createEmpty<IParameterHints> ()
                let sigs = o.Data.Overloads |> Array.map (fun c ->
                    let signature = createEmpty<ISignature> ()
                    let tip = c.Tip.[0].[0]
                    signature.label <-  tip.Signature
                    signature.parameters <- [||]
                        //c.Parameters |> Array.map (fun p ->
                        //    let parameter = createEmpty<IParameter> ()
                        //    parameter.label <- p.Name
                        //    parameter )
                    signature )
                res.currentParameter <- float (o.Data.CurrentParameter)
                res.currentSignature <- 0.
                res.signatures <- sigs
                res )
            |> Promise.toThenable )
        provider

    let activate (disposables: Disposable[]) =
        Globals.ParameterHintsSupport.register("fsharp", createProvider())
        |> ignore

        ()

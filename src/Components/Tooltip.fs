namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.Modes

open DTO
open Events

[<ReflectedDefinition>]
module Tooltip =

    let private createProvider () =
        let provider = createEmpty<IExtraInfoSupport> ()
        provider.``computeInfo <-`` (fun doc pos _ ->
            LanguageService.tooltip (doc.getPath ()) (int pos.line) (int pos.character)
            let promise =
                Globals.Promise.Create(fun (resolve : Func<IComputeExtraInfoResult,_>) error ->
                    TooltipEvent.Publish
                    |> Observable.once (fun o ->
                        let range = doc.getWordRangeAtPosition pos
                        let res = (o.Data |> Array.fold (fun acc n -> (n |> Array.toList) @ acc ) []).Head.Signature
                        let result = createEmpty<IComputeExtraInfoResult> ()
                        result.range <- range
                        result.value <- res
                        resolve.Invoke result )
                    )
                |> unbox<Thenable<_>>
            promise
            )
        provider

    let activate (disposables: Disposable[]) =
        Globals.ExtraInfoSupport.register("fsharp", createProvider())
        |> ignore

        ()

namespace Ionide.VSCode.FSharp

open System
open System.Text.RegularExpressions
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers
open DTO
module node = Fable.Import.Node.Exports

module Implementation =

    type DummyImplementation () =
        interface ImplementationProvider with
            member this.provideImplementation(doc, pos, _) =
                promise {
                    let! res = LanguageService.symbolImplementationProject (doc.fileName) (int pos.line + 1) (int pos.character + 1)
                    return res
                } |> unbox

    let private createProvider () =
        let mapResult (doc : TextDocument) (o : SymbolUseResult) =
            if isNotNull o then
                o.Data.Uses |> Array.map (fun s ->
                    let loc = createEmpty<Location>
                    loc.range <- CodeRange.fromSymbolUse s
                    loc.uri <- Uri.file s.FileName
                    loc  )
                |> ResizeArray
            else
                ResizeArray ()

        { new ImplementationProvider
          with
            member this.provideImplementation(doc, pos, _) =
                promise {
                    let! res = LanguageService.symbolImplementationProject (doc.fileName) (int pos.line + 1) (int pos.character + 1)
                    return mapResult doc res
                } |> unbox
        }


    let activate selector (context : ExtensionContext) =
        languages.registerImplementationProvider(selector, createProvider())
        |> context.subscriptions.Add
        ()


namespace Ionide.VSCode.FSharp

open System
open System.Text
open System.Text.RegularExpressions
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers
open DTO

module TypeDefinition =
    let private mapFindDeclarationResult (doc : TextDocument) (pos : Position) (o : FindDeclarationResult) : Definition option =
        if isNotNull o then
            let loc = createEmpty<Location>
            let range = doc.getWordRangeAtPosition pos
            let length = range.``end``.character - range.start.character
            loc.uri <- Uri.file o.Data.File
            loc.range <- CodeRange.fromDeclaration o.Data length
            loc |> U2.Case1 |> Some
        else
            None

    let private provide (doc : TextDocument) (pos : Position) = promise {
        let! res = LanguageService.findTypeDeclaration (doc.fileName) (int pos.line + 1) (int pos.character + 1)
        return mapFindDeclarationResult doc pos res
    }

    let private createProvider () =
        { new TypeDefinitionProvider
          with
            member this.provideTypeDefinition(doc, pos, ct) =
                promise {
                    return! provide doc pos
                } |> unbox
        }

    let activate selector (context: ExtensionContext) =
        languages.registerTypeDefinitionProvider(selector, createProvider())
        |> context.subscriptions.Add

        ()
namespace Ionide.VSCode.FSharp

open Fable.Core.JsInterop
open Fable.Import.vscode

open DTO
open Ionide.VSCode.Helpers

module HighlightingProvider =
    let private logger = ConsoleAndOutputChannelLogger(Some "Project", Level.DEBUG, None, Some Level.DEBUG)

    let tokenTypes = [|
        "comment"; "string"; "keyword"; "number"; "regexp"; "operator"; "namespace";
        "type"; "struct"; "class"; "interface"; "enum"; "enumMember"; "typeParameter"; "function";
        "member"; "macro"; "variable"; "parameter"; "property"; "label"; "variable.readonly.defaultLibrary";
        "property.readonly";
        "mutable"; "disposable"; "cexpr"; |] //Last row - custom F# specific types

    let legend = SemanticTokensLegend(tokenTypes)

    let provider =
        { new DocumentSemanticTokensProvider
          with
            member __.provideDocumentSemanticTokens(textDocument, ct) =
                promise {
                    let builder = SemanticTokensBuilder(legend)
                    let! res = LanguageService.getHighlighting (textDocument.fileName)
                    res.Data.Highlights
                    |> Array.sortBy(fun n -> n.Range.StartLine * 1000000 + n.Range.StartColumn)
                    |> Array.iter (fun n ->
                        builder.push(CodeRange.fromDTO n.Range, n.TokenType)
                    )

                    return builder.build()
                } |> unbox
        }

    let activate (context : ExtensionContext) =
        let df = createEmpty<DocumentFilter>
        df.language <- Some "fsharp"


        languages.registerDocumentSemanticTokensProvider(!!df, provider, legend )  |> context.subscriptions.Add

        ()

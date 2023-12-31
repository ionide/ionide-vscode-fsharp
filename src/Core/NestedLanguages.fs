namespace Ionide.VSCode.FSharp

open Ionide.VSCode.FSharp.DTO
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode

type VSCUri = Fable.Import.VSCode.Vscode.Uri

[<RequireQualifiedAccess>]
module NestedLanguages =

    type NestedDocument =
        { languageId: string
          content: string
          rangesInParentFile: Range[] }

    type NestedDocuments = System.Collections.Generic.Dictionary<string, NestedDocument>

    let private documentsMap = NestedDocuments()

    let private documentsForFile =
        System.Collections.Generic.Dictionary<string, string[]>()

    let nestedDocumentScheme = "fsharp-nested-document"

    let private makeNestedDocumentName (parent: VSCUri) (languageId: string) (order: int) =
        let uri =
            let parentPathHash = parent.path.GetHashCode()
            vscode.Uri.parse ($"{nestedDocumentScheme}:///{languageId}/{parentPathHash}/{order}.{languageId}", true)

        uri

    let private replaceWithWhitespace (input: string) (builder: System.Text.StringBuilder) =
        input.Split([| '\n'; '\r' |], System.StringSplitOptions.None)
        |> Array.iter (fun line ->
            let s = System.String(' ', line.Length)
            builder.Append(s) |> ignore<System.Text.StringBuilder>)

    let private vscodePos (p: DTO.LSP.Position) : Vscode.Position =
        vscode.Position.Create(p.line, p.character)

    let private vscodeRange (r: DTO.LSP.Range) : Vscode.Range =
        vscode.Range.Create(vscodePos r.start, vscodePos r.``end``)

    let private createSyntheticDocument (parentDocument: TextDocument) (targetSubranges: DTO.LSP.Range[]) : string =

        let fullDocumentRange =
            // create a range that covers the whole document by making a too-long range and having the document trim it to size
            parentDocument.validateRange (vscode.Range.Create(0, 0, parentDocument.lineCount, 0))

        let fullDocumentText = parentDocument.getText ()
        let builder = System.Text.StringBuilder(fullDocumentText.Length)

        match targetSubranges with
        | [||] -> replaceWithWhitespace fullDocumentText builder
        | [| single |] ->
            replaceWithWhitespace
                (parentDocument.getText (vscode.Range.Create(fullDocumentRange.start, vscodePos single.start)))
                builder

            let actualContent = parentDocument.getText (vscodeRange single)
            builder.Append(actualContent) |> ignore<System.Text.StringBuilder>

            replaceWithWhitespace
                (parentDocument.getText (vscode.Range.Create(vscodePos single.``end``, fullDocumentRange.``end``)))
                builder
        | ranges ->
            let mutable currentPos = fullDocumentRange.start
            // foreach range
            // the space from currentPos to range.start is whitespace, copy that in
            // the range is actual content, copy that in
            // set the currentPos to range.end
            // at the end of the ranges, copy in the whitespace from currentPos to fullDocumentRange.end
            for range in ranges do
                let currentToRangeStart = vscode.Range.Create(currentPos, vscodePos range.start)
                replaceWithWhitespace (parentDocument.getText (currentToRangeStart)) builder
                let actualContent = parentDocument.getText (vscodeRange range)
                builder.Append(actualContent) |> ignore<System.Text.StringBuilder>
                currentPos <- vscodePos range.``end``

            let currentToEnd = vscode.Range.Create(currentPos, fullDocumentRange.``end``)
            replaceWithWhitespace (parentDocument.getText (currentToEnd)) builder

        builder.ToString()

    /// given the languages found in a given file, create a synthetic document for each language and track that
    /// document in the documentsMap, clearing the documentsMap of any documents that are no longer needed for that file
    let updateDocuments (languages: NestedLanguagesForFile) =
        promise {
            let parentDocumentUri = vscode.Uri.parse (languages.textDocument.uri, true)
            let! parentDocument = workspace.openTextDocument (parentDocumentUri)

            // create virtual documents
            let nestedDocuments =
                languages.nestedLanguages
                |> Array.mapi (fun order language ->
                    let uri = makeNestedDocumentName parentDocumentUri language.language order
                    let document = createSyntheticDocument parentDocument language.ranges
                    uri.toString (true), language.language, language.ranges |> Array.map vscodeRange, document)

            // track virtual documents with their parent
            let uris = nestedDocuments |> Array.map (fun (fst, _, _, _) -> fst)
            documentsForFile[languages.textDocument.uri] <- uris

            // TODO: remove documents when their parent closes

            // store the virtual contents in our map
            nestedDocuments
            |> Array.iter (fun (uri, language, ranges, document) ->
                documentsMap[uri] <-
                    { languageId = language
                      rangesInParentFile = ranges
                      content = document })
        }

    let tryGetVirtualDocumentInDocAtPosition (parentDocument: TextDocument, position: Position) =
        match documentsForFile.TryGetValue(parentDocument.uri.toString ()) with
        | false, _ -> None
        | true, nestedDocs ->
            nestedDocs
            |> Array.tryPick (fun nestedDocUri ->
                match documentsMap.TryGetValue(nestedDocUri) with
                | false, _ -> None
                | true, nestedDoc ->
                    let containsPos =
                        nestedDoc.rangesInParentFile
                        |> Array.exists (fun range -> range.contains (Fable.Core.U2.Case1 position))

                    if containsPos then
                        Some(vscode.Uri.parse nestedDocUri, nestedDoc.languageId)
                    else
                        None)

    let getAllVirtualDocsForDoc (parentDocument: TextDocument) =
        match documentsForFile.TryGetValue(parentDocument.uri.toString ()) with
        | false, _ -> [||]
        | true, nestedDocs ->
            nestedDocs
            |> Array.choose (fun nestedDocUri ->
                match documentsMap.TryGetValue(nestedDocUri) with
                | false, _ -> None
                | true, nestedDoc -> Some(vscode.Uri.parse nestedDocUri, nestedDoc.languageId))

    type private VirtualDocumentContentProvider() =
        interface TextDocumentContentProvider with
            member this.onDidChange: Event<Uri> option = None

            member this.onDidChange
                with set (v: Event<Uri> option): unit = ()

            member this.provideTextDocumentContent(uri: VSCUri, token: CancellationToken) : ProviderResult<string> =
                match documentsMap.TryGetValue(uri.toString (true)) with
                | false, _ -> None
                | true, nestedDoc -> unbox nestedDoc.content

    let activate (context: ExtensionContext) =
        Notifications.nestedLanguagesDetected.Invoke(fun languages ->
            updateDocuments languages |> ignore<_>
            None)
        |> context.Subscribe

        workspace.registerTextDocumentContentProvider (nestedDocumentScheme, VirtualDocumentContentProvider())
        |> context.Subscribe

        ()

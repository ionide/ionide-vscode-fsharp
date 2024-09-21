namespace Ionide.VSCode.FSharp

open Fable.Core.JsInterop
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode

module CodeLensHelpers =

    // This data structure provided by FSAC, in CodeLensResolve:
    // https://github.com/ionide/FsAutoComplete/blob/258fbc55053f585ea6ebd81a7dc10e4f3573aff6/src/FsAutoComplete/LspServers/AdaptiveFSharpLspServer.fs#L1637

    type LspUri = string

    type LspPosition = {
        line : uint32
        character : uint32
    }

    type LspRange = {
        start : LspPosition
        ``end`` : LspPosition
    }

    type LspLocation = {
        uri : string
        range : LspRange
    }

    type CustomIExports =
        abstract registerCommand:
            command: string * callback: (LspUri -> LspPosition -> LspLocation seq -> obj option) * ?thisArg: obj -> Disposable

    let showReferences (uri: LspUri) (args2: LspPosition) (args3: LspLocation seq) =
        let uri = vscode.Uri.parse !!uri
        let pos = vscode.Position.Create(float args2.line, float args2.character)

        let locs =
            args3
            |> Seq.map (fun f ->
                let uri = vscode.Uri.parse f.uri

                let range =
                    vscode.Range.Create(
                        float f.range.start.line,
                        float f.range.start.character,
                        float f.range.``end``.line,
                        float f.range.``end``.character
                    )

                vscode.Location.Create(uri, !^range))
            |> ResizeArray

        commands.executeCommand ("editor.action.showReferences", Some(box uri), Some(box pos), Some(box locs))

    let activate (context: ExtensionContext) =
        (unbox<CustomIExports> commands)
            .registerCommand ("fsharp.showReferences", unbox<(LspUri -> LspPosition -> LspLocation seq -> obj option)> (showReferences))
        |> context.Subscribe

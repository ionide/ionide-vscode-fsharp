namespace Ionide.VSCode.FSharp

open Fable.Core.JsInterop
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode

module CodeLensHelpers =

    type CustomIExports =
        abstract registerCommand:
            command: string * callback: (obj -> obj -> obj -> obj option) * ?thisArg: obj -> Disposable

    let showReferences (args: obj) (args2: obj) (args3: obj) =
        let uri = vscode.Uri.parse !!args
        let pos = vscode.Position.Create(!!args2?Line, !!args2?Character)

        let locs =
            !!args3
            |> Seq.map (fun f ->
                let uri = vscode.Uri.parse !!f?Uri

                let range =
                    vscode.Range.Create(
                        !!f?Range?Start?Line,
                        !!f?Range?Start?Character,
                        !!f?Range?End?Line,
                        !!f?Range?End?Character
                    )

                vscode.Location.Create(uri, !^range))
            |> ResizeArray

        commands.executeCommand ("editor.action.showReferences", Some(box uri), Some(box pos), Some(box locs))

    let activate (context: ExtensionContext) =
        (unbox<CustomIExports> commands)
            .registerCommand ("fsharp.showReferences", unbox<(obj -> obj -> obj -> obj option)> (showReferences))
        |> context.Subscribe

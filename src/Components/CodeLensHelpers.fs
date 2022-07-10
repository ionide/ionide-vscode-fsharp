namespace Ionide.VSCode.FSharp

open System
open Fable.Core.JsInterop
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode

module CodeLensHelpers =

    let showReferences (args: string) (args2: obj) (args3: obj[]) =
        let uri = vscode.Uri.parse args
        let pos = vscode.Position.Create(!!args2?Line, !!args2?Character)

        let locs =
            args3
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
        commands.registerCommand ("fsharp.showReferences", showReferences |> objfy4)
        |> context.Subscribe

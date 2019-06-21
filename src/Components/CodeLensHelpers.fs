namespace Ionide.VSCode.FSharp

open System
open Fable.Core.JsInterop
open Fable.Import.vscode
open Fable.Import.Node

module node = Fable.Import.Node.Exports

module CodeLensHelpers =

    let showReferences (args: string) (args2: obj) (args3: obj[])   =
        let uri = Uri.parse args
        let pos = Position(!!args2?Line, !!args2?Character)
        let locs =
            args3
            |> Seq.map (fun f ->
                let uri = Uri.parse !!f?Uri
                let range = Range(!!f?Range?Start?Line, !!f?Range?Start?Character, !!f?Range?End?Line, !!f?Range?End?Character)
                Location(uri, !^ range)
            )
            |> ResizeArray
        commands.executeCommand("editor.action.showReferences", uri, pos, locs )

    let activate (context : ExtensionContext) =

        commands.registerCommand("fsharp.showReferences", showReferences |> unbox<Func<obj,obj, obj, obj>> ) |> context.subscriptions.Add
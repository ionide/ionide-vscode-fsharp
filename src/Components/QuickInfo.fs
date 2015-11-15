namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.languages

open DTO
open Ionide.VSCode.Helpers

[<ReflectedDefinition>]
module QuickInfo =
    let mutable private item : StatusBarItem option = None

    

    let private handle' (event : TextEditorSelectionChangeEvent) = 
        let doc = event.textEditor.document
        let pos = event.selections.[0].active
        LanguageService.tooltip (doc.fileName) (int pos.line + 1) (int pos.character + 1)
        |> Promise.success (fun o -> 
            let res = (o.Data |> Array.fold (fun acc n -> (n |> Array.toList) @ acc ) []).Head.Signature
            let t = res.Split('\n').[0] 
            item |> Option.iter (fun n -> n.hide ())
            let i = window.Globals.createStatusBarItem (1 |> unbox)
            i.text <- t
            i.tooltip <- res
            i.show ()
            item <- Some i
            ()
        )
        |> ignore
        
    let mutable private timer = None : NodeJS.Timer option
    
    let private handle (event : TextEditorSelectionChangeEvent) =
        timer |> Option.iter(Globals.clearTimeout)
        timer <- Some (Globals.setTimeout((fun n -> handle' event), 500.) )

    let activate (disposables: Disposable[]) = 
        window.Globals.onDidChangeTextEditorSelection
        |> EventHandler.add handle () disposables 
        ()
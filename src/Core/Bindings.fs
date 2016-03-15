namespace FunScript.TypeScript.vscode

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode

type TextDocumentContentProvider = interface end
type EventEmitter<'T> = interface end

[<AutoOpen>]
module Bindings =

    type TextDocumentContentProvider with
        [<FunScript.JSEmitInline "({0}.provideTextDocumentContent({1}, {2}))">]
        member __.provideTextDocumentContent(uri : Uri, token : CancellationToken) : Thenable<string> = failwith "JS"
        [<FunScript.JSEmitInline "({0}.provideTextDocumentContent = {1})">]
        member __.``provideTextDocumentContent <-``(func : System.Func<Uri* CancellationToken , Thenable<string>>) : unit = failwith "JS"

        [<FunScript.JSEmitInline "({0}.onDidChange)">]
        member __.onDidChange with get() : vscode.Event<Uri> = failwith "JS" and set (v : vscode.Event<Uri>) : unit = failwith "JS"

    type EventEmitter<'T> with
        [<FunScript.JSEmitInline "({0}.event)">]
        member __.event : vscode.Event<'T> = failwith "JS"

        [<FunScript.JSEmitInline "({0}.fire({1}))">]
        member __.fire(a : 'T) : unit = failwith "JS"

        [<FunScript.JSEmitInline "(new vscode.EventEmitter())">]
        static member Create() : EventEmitter<'T> = failwith "JS"
        
    type WorkspaceEdit with
        [<FunScript.JSEmitInline "(new vscode.WorkspaceEdit())">]
        static member Create() : WorkspaceEdit = failwith "JS"


namespace FunScript.TypeScript.vscode

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode

type TextDocumentContentProvider = interface end

[<AutoOpen>]
module Bindings =

    type TextDocumentContentProvider with
        [<FunScript.JSEmitInline "({0}.onDidStopChanging({1}, {2}))">]
        member __.provideTextDocumentContent(uri : Uri, token : CancellationToken) : Thenable<string> = failwith "JS"
        [<FunScript.JSEmitInline "({0}.onDidStopChanging = {1})">]
        member __.``provideTextDocumentContent <-``(func : System.Func<Uri* CancellationToken , Thenable<string>>) : unit = failwith "JS"

    type workspace.Globals with
        member __.registerTextDocumentContentProvider(scheme : string, provider : TextDocumentContentProvider) : Disposable = failwith "JS"
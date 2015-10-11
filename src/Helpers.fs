namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode

[<AutoOpen>]
[<ReflectedDefinition>]
module Helpers =

    [<AutoOpen>]
    module Bindings =
        type EventDelegate<'T> with
            [<FunScript.JSEmitInline("({0}({1}, {2}, {3}))")>]
            member __.Add(f : 'T -> _, args : obj, disposables : Disposable[]) : unit = failwith "JS"

    module EventHandler =
        let add (f : 'T -> _) (args : obj) (disposables : Disposable[]) (ev : EventDelegate<'T>) =
            ev.Add(f,args,disposables)

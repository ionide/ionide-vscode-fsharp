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

    module Promise =
        let success (a : 'T -> 'R) (pr : Promise<'T>) : Promise<'R> =
            pr._then (unbox a) |> unbox

        let fail (a : obj -> 'T)  (pr : Promise<'T>) : Promise<'T> =
            pr._catch (unbox a)

        let either (a : 'T -> 'R) (b : obj -> 'R)  (pr : Promise<'T>) : Promise<'R> =
            pr._then (unbox a, unbox b) |> unbox

        let toPromise (a : Thenable<'T>) = a |> unbox<Promise<'T>>

        let toThenable (a : Promise<'T>) = a |> unbox<Thenable<'T>>

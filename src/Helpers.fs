namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode

[<AutoOpen>]
[<ReflectedDefinition>]
module Helpers =

    [<AutoOpen>]
    module Dynamic =
        [<JSEmitInline("({0}[{1}])")>]
        let (?) (ob: obj) (prop: string): 'a = failwith "never"

        [<JSEmitInline("({0}[{1}] = {2})")>]
        let (?<-) (ob: obj) (prop: string) (value: 'a): unit = failwith "never"

    module JS =
        //[<Literal>]
        //let byString =
        //    """ function({1}, {0}) {
        //        {1} = {1}.replace(/\[(\w+)\]/g, '.$1'); // convert indexes to properties
        //        {1} = {1}.replace(/^\./, '');           // strip a leading dot
        //        var a = {1}.split('.');
        //        for (var i = 0, n = a.length; i < n; ++i) {
        //            var k = a[i];
        //            if (k in o) {
        //                {0} = {0}[k];
        //            } else {
        //                return;
        //            }
        //        }
        //        return {o};
        //    } """
        //
        //[<JSEmit(byString)>]
        //let getPropertyByString<'T> (prop:string) (o:obj) : 'T = failwith "JS"

        [<JSEmitInline("({1}[{0}])")>]
        let getProperty<'T> (prop:string) (o:obj) : 'T = failwith "JS"

        [<JSEmitInline("({0}[{1}] != undefined)")>]
        let isPropertyDefined (o: obj) (key: string) : bool = failwith "JS"

        [<JSEmitInline("(global[{0}] != undefined)")>]
        let isGloballyDefined (key: string) : bool = failwith "never"

        [<JSEmitInline("({0} != undefined)")>]
        let isDefined (o: obj) : bool = failwith "never"

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

    module VSCode =
        let getPluginPath pluginName =
            if Globals._process.platform = "win32" then
                Globals._process.env?USERPROFILE + @"\.vscode\extensions\" + pluginName
            else
                Globals._process.env?HOME + "/.vscode/extensions/" + pluginName

    //module Proces

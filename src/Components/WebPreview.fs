namespace Ionide.VSCode.FSharp

open System
open FunScript
open FunScript.TypeScript
open FunScript.TypeScript.vscode
open FunScript.TypeScript.vscode.languages
open FunScript.TypeScript.child_process
open FunScript.TypeScript.fs

open DTO
open Ionide.VSCode.Helpers


[<ReflectedDefinition>]
module WebPreview =



    let activate (disposables : Disposable[]) =
        ()
module Ionide.VSCode.Generator

// --------------------------------------------------------------------------------------
// Load the F# implementation and specify parameters for the translator
// --------------------------------------------------------------------------------------


// Root directory, relatively to which files are saved
let root = __SOURCE_DIRECTORY__

// --------------------------------------------------------------------------------------
// Compile F# type to an atom module
// --------------------------------------------------------------------------------------

open System.Reflection
open Microsoft.FSharp.Quotations
open FunScript.Compiler

let translateModules (typ : System.Type) fileName =
    // We generate F# quotation that returns all the methods that we want to expose
    // from the class. This way, we can then wrap it into simple JS code that
    // creates the module. The generated quotation looks something like this:
    //
    //   [| box (fun () -> new WordCount());
    //      box (fun (self:WordCount) a1 .. an -> self.activate(a1, .., an))
    //      ... and so on for all other methods .. |]
    //
    let ctor = typ.GetConstructor([||])
    let meths = typ.GetMethods(BindingFlags.DeclaredOnly ||| BindingFlags.Public ||| BindingFlags.Instance)

    /// Creates "(fun p1 .. pn -> <body>)" and "[p1; ..; pn]"
    /// (which is used when generating boxed lambdas that pass parameters to the actual function)
    let createParameterPassing (m:MethodBase) =
      let paramVars = m.GetParameters() |> Array.mapi (fun i p -> Var(sprintf "p%d" i, p.ParameterType))
      let paramArgs = [ for v in paramVars -> Expr.Var(v) ]
      let lambdaConstr = paramVars |> Seq.fold (fun fn var -> fun body -> Expr.Lambda(var, fn body)) id
      lambdaConstr, paramArgs

    let exportFunctions =
      [ for m in meths ->
          let tv = new Var("this", typ)
          let lambdaConstr, paramArgs = createParameterPassing m
          Expr.Lambda(tv, lambdaConstr (Expr.Call(Expr.Var(tv), m, paramArgs))) ]

    let exportCtor =
      Expr.Coerce
        ( Expr.Lambda(Var("ign", typeof<unit>), Expr.NewObject(typ.GetConstructor [||], [])),
          typeof<obj> )

    let functionArray =
      Expr.NewArray(typeof<obj>, exportCtor::[ for f in exportFunctions -> Expr.Coerce(f, typeof<obj>)])

    let coreJS = Compiler.Compile(functionArray)

    // Now we just wrap the generated JavaScript into 'wrappedFunScript' function
    // Then we call the function and create a module export with all the public methods
    // from the provided type (just by calling one of the functions from the array)
    let moduleJS =
      [ yield "var vscode = require('vscode');"
        yield "var child_process = require('child_process');"
        yield "var XMLHttpRequest = require('xhr2');"
        yield "var fs = require('fs');"
        yield "var path = require('path');"
        yield "var toml = require('toml');"
        yield "var window = {
                    Math: Math,
                    JSON: JSON,
                    XMLHttpRequest : XMLHttpRequest,
                    console: console,
                    Promise : Promise,
                    process: process,
                    setTimeout: setTimeout,
                    clearTimeout: clearTimeout };"
        yield ""
        yield "function wrappedFunScript() { \n" + coreJS + "\n }"
        yield "var _funcs = wrappedFunScript();"
        yield "var _self = _funcs[0]();"
        yield ""
        yield "exports.activate = _funcs[1](_self);"
        yield "exports.deactivate = _funcs[2](_self);" ]
      |> String.concat "\n"
    System.IO.File.WriteAllText(System.IO.Path.Combine(root, fileName), moduleJS)

// --------------------------------------------------------------------------------------
// Entry point - do stuff!
// --------------------------------------------------------------------------------------

do translateModules typeof<Ionide.VSCode.FSharp> "../release/fsharp.js"

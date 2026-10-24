/// Bindings for the parts of the marked npm package that Ionide uses, written against marked's lib/marked.d.ts.
module Marked

open Fable.Core

type IExports =
    /// Compiles markdown to HTML, with GitHub Flavored Markdown (tables included) on by default.
    /// With async false, marked returns the HTML itself instead of a promise.
    abstract parse: src: string * options: {| async: bool |} -> string

[<Import("marked", "marked")>]
let marked: IExports = jsNative

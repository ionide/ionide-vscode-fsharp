/// Bindings for the parts of the semver npm package that Ionide uses, written against @types/semver.
module Semver

open Fable.Core

/// A parsed version, see @types/semver/classes/semver.d.ts.
[<AllowNullLiteral>]
type SemVer =
    abstract raw: string
    abstract major: float
    abstract minor: float
    abstract prerelease: System.Collections.Generic.IReadOnlyList<U2<string, float>>

type IExports =
    /// Returns the parsed version, or None if it isn't valid.
    abstract parse: version: string -> SemVer option

    /// Compares two versions excluding build identifiers: 0 if v1 = v2, 1 if v1 is greater, -1 if v2 is greater.
    abstract compare: v1: SemVer * v2: SemVer -> int

[<Import("*", "semver")>]
let semver: IExports = jsNative

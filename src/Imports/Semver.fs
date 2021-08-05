// ts2fable 0.8.0-build.618
module rec Semver

#nowarn "3390" // disable warnings for invalid XML comments

open System
open Fable.Core
open Fable.Core.JS

type ReadonlyArray<'T> = System.Collections.Generic.IReadOnlyList<'T>


type [<AllowNullLiteral>] IExports =
    abstract Comparator: ComparatorStatic
    abstract Range: RangeStatic
    abstract SemVer: SemVerStatic
    /// Returns cleaned (removed leading/trailing whitespace, remove '=v' prefix) and parsed version, or null if version is invalid.
    abstract clean: version: string * ?optionsOrLoose: U2<bool, Semver.Options> -> string option

    /// Pass in a comparison string, and it'll call the corresponding semver comparison function.
    /// "===" and "!==" do simple string comparison, but are included for completeness.
    /// Throws if an invalid comparison string is provided.
    abstract cmp: v1: U2<string, SemVer> * operator: Semver.Operator * v2: U2<string, SemVer> * ?optionsOrLoose: U2<bool, Semver.Options> -> bool

    /// Coerces a string to SemVer if possible
    abstract coerce: version: U3<string, float, SemVer> option * ?options: Semver.CoerceOptions -> SemVer option
    /// <summary>
    /// Compares two versions including build identifiers (the bit after <c>+</c> in the semantic version string).
    ///
    /// Sorts in ascending order when passed to <c>Array.sort()</c>.
    /// </summary>
    /// <returns>
    /// - <c>0</c> if <c>v1</c> == <c>v2</c>
    /// - <c>1</c> if <c>v1</c> is greater
    /// - <c>-1</c> if <c>v2</c> is greater.
    /// </returns>
    abstract compareBuild: a: U2<string, SemVer> * b: U2<string, SemVer> -> CompareBuildReturn

    abstract compareLoose: v1: U2<string, SemVer> * v2: U2<string, SemVer> -> CompareLooseReturn
    /// <summary>
    /// Compares two versions excluding build identifiers (the bit after <c>+</c> in the semantic version string).
    ///
    /// Sorts in ascending order when passed to <c>Array.sort()</c>.
    /// </summary>
    /// <returns>
    /// - <c>0</c> if <c>v1</c> == <c>v2</c>
    /// - <c>1</c> if <c>v1</c> is greater
    /// - <c>-1</c> if <c>v2</c> is greater.
    /// </returns>
    abstract compare: v1: U2<string, SemVer> * v2: U2<string, SemVer> * ?optionsOrLoose: U2<bool, Semver.Options> -> CompareReturn
    /// Returns difference between two versions by the release type (major, premajor, minor, preminor, patch, prepatch, or prerelease), or null if the versions are the same.
    abstract diff: v1: U2<string, SemVer> * v2: U2<string, SemVer> * ?optionsOrLoose: U2<bool, Semver.Options> -> Semver.ReleaseType option

    /// v1 == v2 This is true if they're logically equivalent, even if they're not the exact same string. You already know how to compare strings.
    abstract eq: v1: U2<string, SemVer> * v2: U2<string, SemVer> * ?optionsOrLoose: U2<bool, Semver.Options> -> bool

    /// v1 > v2
    abstract gt: v1: U2<string, SemVer> * v2: U2<string, SemVer> * ?optionsOrLoose: U2<bool, Semver.Options> -> bool

    /// v1 >= v2
    abstract gte: v1: U2<string, SemVer> * v2: U2<string, SemVer> * ?optionsOrLoose: U2<bool, Semver.Options> -> bool

    /// Return the version incremented by the release type (major, minor, patch, or prerelease), or null if it's not valid.
    abstract inc: version: U2<string, SemVer> * release: Semver.ReleaseType * ?optionsOrLoose: U2<bool, Semver.Options> * ?identifier: string -> string option
    abstract inc: version: U2<string, SemVer> * release: Semver.ReleaseType * ?identifier: string -> string option

    /// v1 < v2
    abstract lt: v1: U2<string, SemVer> * v2: U2<string, SemVer> * ?optionsOrLoose: U2<bool, Semver.Options> -> bool

    /// v1 <= v2
    abstract lte: v1: U2<string, SemVer> * v2: U2<string, SemVer> * ?optionsOrLoose: U2<bool, Semver.Options> -> bool

    /// Return the major version number.
    abstract major: version: U2<string, SemVer> * ?optionsOrLoose: U2<bool, Semver.Options> -> float

    /// Return the minor version number.
    abstract minor: version: U2<string, SemVer> * ?optionsOrLoose: U2<bool, Semver.Options> -> float

    /// v1 != v2 The opposite of eq.
    abstract neq: v1: U2<string, SemVer> * v2: U2<string, SemVer> * ?optionsOrLoose: U2<bool, Semver.Options> -> bool

    /// Return the parsed version as a SemVer object, or null if it's not valid.
    abstract parse: version: U2<string, SemVer> option * ?optionsOrLoose: U2<bool, Semver.Options> -> SemVer option

    /// Return the patch version number.
    abstract patch: version: U2<string, SemVer> * ?optionsOrLoose: U2<bool, Semver.Options> -> float

    /// Returns an array of prerelease components, or null if none exist.
    abstract prerelease: version: U2<string, SemVer> * ?optionsOrLoose: U2<bool, Semver.Options> -> ResizeArray<U2<string, float>> option

    /// <summary>
    /// The reverse of compare.
    ///
    /// Sorts in descending order when passed to <c>Array.sort()</c>.
    /// </summary>
    abstract rcompare: v1: U2<string, SemVer> * v2: U2<string, SemVer> * ?optionsOrLoose: U2<bool, Semver.Options> -> RcompareReturn

    /// <summary>Sorts an array of semver entries in descending order using <c>compareBuild()</c>.</summary>
    abstract rsort: list: ResizeArray<'T> * ?optionsOrLoose: U2<bool, Semver.Options> -> ResizeArray<'T>

    /// Return true if the version satisfies the range.
    abstract satisfies: version: U2<string, SemVer> * range: U2<string, Range> * ?optionsOrLoose: U2<bool, Semver.Options> -> bool

    /// <summary>Sorts an array of semver entries in ascending order using <c>compareBuild()</c>.</summary>
    abstract sort: list: ResizeArray<'T> * ?optionsOrLoose: U2<bool, Semver.Options> -> ResizeArray<'T>

    /// Return the parsed version as a string, or null if it's not valid.
    abstract valid: version: U2<string, SemVer> option * ?optionsOrLoose: U2<bool, Semver.Options> -> string option

    /// <summary>
    /// Compares two identifiers, must be numeric strings or truthy/falsy values.
    ///
    /// Sorts in ascending order when passed to <c>Array.sort()</c>.
    /// </summary>
    abstract compareIdentifiers: a: string option * b: string option -> CompareIdentifiersReturn
    /// <summary>
    /// The reverse of compareIdentifiers.
    ///
    /// Sorts in descending order when passed to <c>Array.sort()</c>.
    /// </summary>
    abstract rcompareIdentifiers: a: string option * b: string option -> RcompareIdentifiersReturn
    /// Return true if version is greater than all the versions possible in the range.
    abstract gtr: version: U2<string, SemVer> * range: U2<string, Range> * ?optionsOrLoose: U2<bool, Semver.Options> -> bool

    /// Return true if any of the ranges comparators intersect
    abstract intersects: range1: U2<string, Range> * range2: U2<string, Range> * ?optionsOrLoose: U2<bool, Semver.Options> -> bool

    /// Return true if version is less than all the versions possible in the range.
    abstract ltr: version: U2<string, SemVer> * range: U2<string, Range> * ?optionsOrLoose: U2<bool, Semver.Options> -> bool

    /// Return the highest version in the list that satisfies the range, or null if none of them do.
    abstract maxSatisfying: versions: ResizeArray<'T> * range: U2<string, Range> * ?optionsOrLoose: U2<bool, Semver.Options> -> 'T option

    /// Return the lowest version in the list that satisfies the range, or null if none of them do.
    abstract minSatisfying: versions: ResizeArray<'T> * range: U2<string, Range> * ?optionsOrLoose: U2<bool, Semver.Options> -> 'T option

    /// Return the lowest version that can possibly match the given range.
    abstract minVersion: range: U2<string, Range> * ?optionsOrLoose: U2<bool, Semver.Options> -> SemVer option

    /// Return true if the version is outside the bounds of the range in either the high or low direction.
    /// The hilo argument must be either the string '>' or '<'. (This is the function called by gtr and ltr.)
    abstract outside: version: U2<string, SemVer> * range: U2<string, Range> * hilo: OutsideHilo * ?optionsOrLoose: U2<bool, Semver.Options> -> bool

    /// <summary>
    /// Return a "simplified" range that matches the same items in <c>versions</c> list as the range specified.
    /// Note that it does *not* guarantee that it would match the same versions in all cases,
    /// only for the set of versions provided.
    /// This is useful when generating ranges by joining together multiple versions with <c>||</c> programmatically,
    /// to provide the user with something a bit more ergonomic.
    /// If the provided range is shorter in string-length than the generated range, then that is returned.
    /// </summary>
    abstract simplify: ranges: ResizeArray<string> * range: U2<string, Range> * ?options: Semver.Options -> U2<string, Range>

    /// Return true if the subRange range is entirely contained by the superRange range.
    abstract subset: sub: U2<string, Range> * dom: U2<string, Range> * ?options: Semver.Options -> bool

    /// Mostly just for testing and legacy API reasons
    abstract toComparators: range: U2<string, Range> * ?optionsOrLoose: U2<bool, Semver.Options> -> string

    /// Return the valid range or null if it's not valid
    abstract validRange: range: U2<string, Range> option * ?optionsOrLoose: U2<bool, Semver.Options> -> string option

type [<AllowNullLiteral>] Comparator =
    abstract semver: SemVer with get, set
    abstract operator: ComparatorOperator with get, set
    abstract value: string with get, set
    abstract loose: bool with get, set
    abstract options: Semver.Options with get, set
    abstract parse: comp: string -> unit
    abstract test: version: U2<string, SemVer> -> bool
    abstract intersects: comp: Comparator * ?optionsOrLoose: U2<bool, Semver.Options> -> bool

type [<AllowNullLiteral>] ComparatorStatic =
    [<EmitConstructor>] abstract Create: comp: U2<string, Comparator> * ?optionsOrLoose: U2<bool, Semver.Options> -> Comparator

type [<StringEnum>] [<RequireQualifiedAccess>] ComparatorOperator =
    | [<CompiledName "">] Empty
    | [<CompiledName "=">] Eq
    | [<CompiledName "<">] LT
    | [<CompiledName ">">] GT
    | [<CompiledName "<=">] LTE
    | [<CompiledName ">=">] GTE


type [<AllowNullLiteral>] Range =
    abstract range: string with get, set
    abstract raw: string with get, set
    abstract loose: bool with get, set
    abstract options: Semver.Options with get, set
    abstract includePrerelease: bool with get, set
    abstract format: unit -> string
    abstract inspect: unit -> string
    abstract set: ReadonlyArray<ReadonlyArray<Comparator>> with get, set
    abstract parseRange: range: string -> ResizeArray<Comparator>
    abstract test: version: U2<string, SemVer> -> bool
    abstract intersects: range: Range * ?optionsOrLoose: U2<bool, Semver.Options> -> bool

type [<AllowNullLiteral>] RangeStatic =
    [<EmitConstructor>] abstract Create: range: U2<string, Range> * ?optionsOrLoose: U2<bool, Semver.Options> -> Range

type [<AllowNullLiteral>] SemVer =
    abstract raw: string with get, set
    abstract loose: bool with get, set
    abstract options: Semver.Options with get, set
    abstract format: unit -> string
    abstract inspect: unit -> string
    abstract major: float with get, set
    abstract minor: float with get, set
    abstract patch: float with get, set
    abstract version: string with get, set
    abstract build: ReadonlyArray<string> with get, set
    abstract prerelease: ReadonlyArray<U2<string, float>> with get, set
    /// <summary>Compares two versions excluding build identifiers (the bit after <c>+</c> in the semantic version string).</summary>
    /// <returns>
    /// - <c>0</c> if <c>this</c> == <c>other</c>
    /// - <c>1</c> if <c>this</c> is greater
    /// - <c>-1</c> if <c>other</c> is greater.
    /// </returns>
    abstract compare: other: U2<string, SemVer> -> SemVerCompareReturn
    /// <summary>Compares the release portion of two versions.</summary>
    /// <returns>
    /// - <c>0</c> if <c>this</c> == <c>other</c>
    /// - <c>1</c> if <c>this</c> is greater
    /// - <c>-1</c> if <c>other</c> is greater.
    /// </returns>
    abstract compareMain: other: U2<string, SemVer> -> SemVerCompareMainReturn
    /// <summary>Compares the prerelease portion of two versions.</summary>
    /// <returns>
    /// - <c>0</c> if <c>this</c> == <c>other</c>
    /// - <c>1</c> if <c>this</c> is greater
    /// - <c>-1</c> if <c>other</c> is greater.
    /// </returns>
    abstract comparePre: other: U2<string, SemVer> -> SemVerComparePreReturn
    /// <summary>Compares the build identifier of two versions.</summary>
    /// <returns>
    /// - <c>0</c> if <c>this</c> == <c>other</c>
    /// - <c>1</c> if <c>this</c> is greater
    /// - <c>-1</c> if <c>other</c> is greater.
    /// </returns>
    abstract compareBuild: other: U2<string, SemVer> -> SemVerCompareBuildReturn
    abstract inc: release: Semver.ReleaseType * ?identifier: string -> SemVer

type [<RequireQualifiedAccess>] SemVerCompareReturn =
    | N1 = 1
    | N0 = 0

type [<RequireQualifiedAccess>] SemVerCompareMainReturn =
    | N1 = 1
    | N0 = 0

type [<RequireQualifiedAccess>] SemVerComparePreReturn =
    | N1 = 1
    | N0 = 0

type [<RequireQualifiedAccess>] SemVerCompareBuildReturn =
    | N1 = 1
    | N0 = 0

type [<AllowNullLiteral>] SemVerStatic =
    [<EmitConstructor>] abstract Create: version: U2<string, SemVer> * ?optionsOrLoose: U2<bool, Semver.Options> -> SemVer

type [<RequireQualifiedAccess>] CompareBuildReturn =
    | N1 = 1
    | N0 = 0

type [<RequireQualifiedAccess>] CompareLooseReturn =
    | N1 = 1
    | N0 = 0

type [<RequireQualifiedAccess>] CompareReturn =
    | N1 = 1
    | N0 = 0


type [<RequireQualifiedAccess>] RcompareReturn =
    | N1 = 1
    | N0 = 0

type [<RequireQualifiedAccess>] CompareIdentifiersReturn =
    | N1 = 1
    | N0 = 0

type [<RequireQualifiedAccess>] RcompareIdentifiersReturn =
    | N1 = 1
    | N0 = 0

type [<StringEnum>] [<RequireQualifiedAccess>] OutsideHilo =
    | [<CompiledName ">">] GT
    | [<CompiledName "<">] LT

let [<Import("SEMVER_SPEC_VERSION","semver")>] SEMVER_SPEC_VERSION: string = jsNative

type [<StringEnum>] [<RequireQualifiedAccess>] ReleaseType =
    | Major
    | Premajor
    | Minor
    | Preminor
    | Patch
    | Prepatch
    | Prerelease

type [<AllowNullLiteral>] Options =
    abstract loose: bool option with get, set
    abstract includePrerelease: bool option with get, set

type [<AllowNullLiteral>] CoerceOptions =
    inherit Options
    /// <summary>Used by <c>coerce()</c> to coerce from right to left.</summary>
    /// <default>false</default>
    /// <example>
    /// coerce('1.2.3.4', { rtl: true });
    /// // =&gt; SemVer { version: '2.3.4', ... }
    /// </example>
    abstract rtl: bool option with get, set

type [<StringEnum>] [<RequireQualifiedAccess>] Operator =
    | [<CompiledName "===">] ExactlyEqual
    | [<CompiledName "!==">] ExactlyUnequal
    | [<CompiledName "">] Empty
    | [<CompiledName "=">] EQ
    | [<CompiledName "==">] DoubleEqual
    | [<CompiledName "!=">] NE
    | [<CompiledName ">">] GT
    | [<CompiledName ">=">] GTE
    | [<CompiledName "<">] LT
    | [<CompiledName "<=">] LTE

let [<Import("*","semver")>] semver: Semver.IExports = jsNative

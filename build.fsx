// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#I "packages/FAKE/tools"
#r "packages/FAKE/tools/FakeLib.dll"
open System
open System.Diagnostics
open System.IO
open Fake
open Fake.Git
open Fake.ProcessHelper
open Fake.ReleaseNotesHelper
open Fake.ZipHelper

#if MONO
#else

#load "src/vscode-bindings.fsx"
#load "src/Helpers.fs"
#load "src/Core/DTO.fs"
#load "src/Core/LanguageService.fs"
#load "src/Components/Linter.fs"
#load "src/Components/Tooltip.fs"
#load "src/Components/Autocomplete.fs"
#load "src/Components/ParameterHints.fs"
#load "src/Components/Declaration.fs"
#load "src/Components/Occurrences.fs"
#load "src/fsharp.fs"
#load "src/main.fs"
#endif


// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "ionide"
let gitHome = "https://github.com/" + gitOwner


// The name of the project on GitHub
let gitName = "ionide-vscode-fsharp"

// The url for the raw files hosted
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/ionide"


// Read additional information from the release notes document
let releaseNotesData =
    File.ReadAllLines "RELEASE_NOTES.md"
    |> parseAllReleaseNotes

let release = List.head releaseNotesData

let msg =  release.Notes |> List.fold (fun r s -> r + s + "\n") ""
let releaseMsg = (sprintf "Release %s\n" release.NugetVersion) + msg


let run cmd args dir =
    if execProcess( fun info ->
        info.FileName <- cmd
        if not( String.IsNullOrWhiteSpace dir) then
            info.WorkingDirectory <- dir
        info.Arguments <- args
    ) System.TimeSpan.MaxValue = false then
        traceError <| sprintf "Error while running '%s' with args: %s" cmd args


// --------------------------------------------------------------------------------------
// Build the Generator project and run it
// --------------------------------------------------------------------------------------

Target "Clean" (fun _ ->
    CopyFiles "release" ["README.md"; "LICENSE"; "RELEASE_NOTES.md"]
)

#if MONO
Target "BuildGenerator" (fun () ->
    [ __SOURCE_DIRECTORY__ @@ "src" @@ "Ionide.FSharp.fsproj" ]
    |> MSBuildDebug "" "Rebuild"
    |> Log "AppBuild-Output: "
)

Target "RunGenerator" (fun () ->
    (TimeSpan.FromMinutes 5.0)
    |> ProcessHelper.ExecProcess (fun p ->
        p.FileName <- __SOURCE_DIRECTORY__ @@ "src" @@ "bin" @@ "Debug" @@ "Ionide.FSharp.exe" )
    |> ignore
)
#else
Target "RunScript" (fun () ->
    Ionide.VSCode.Generator.translateModules typeof<Ionide.VSCode.FSharp> "../release/fsharp.js"
)
#endif

// --------------------------------------------------------------------------------------
// Run generator by default. Invoke 'build <Target>' to override
// --------------------------------------------------------------------------------------

Target "Default" DoNothing

#if MONO
"Clean"
    ==> "BuildGenerator"
    ==> "RunGenerator"
    ==> "Default"
#else
"Clean"
    ==> "RunScript"
    ==> "Default"
#endif

RunTargetOrDefault "Default"

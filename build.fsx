#!/usr/bin/env -S dotnet fsi --
#r "nuget: Fun.Build, 1.2.0"
#r "nuget: Fake.IO.FileSystem, 6.2.0"

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open Fake.IO
open Fake.IO.FileSystemOperators
open Fun.Build

// The relative paths below, and the file operations of the stages, resolve from the repository root.
Directory.SetCurrentDirectory __SOURCE_DIRECTORY__

let repository = "ionide/ionide-vscode-fsharp"
let releaseDir = __SOURCE_DIRECTORY__ </> "release"
let tempDir = __SOURCE_DIRECTORY__ </> "temp"
let fableOutDir = __SOURCE_DIRECTORY__ </> "out"

/// The target frameworks of the FSAC builds that ship in the extension.
let fsacTfms = [ "net10.0" ]

/// The newest entry of RELEASE_NOTES.md: its version and the notes below its heading.
/// The headings read `### 8.0.1 - 24.10.2026`, the date is optional.
let latestReleaseNotes () : string * string =
    let lines = File.ReadAllLines(__SOURCE_DIRECTORY__ </> "RELEASE_NOTES.md")
    let heading = Regex @"^###\s+(?<version>\d[^\s,]*)"

    let headings =
        lines
        |> Array.indexed
        |> Array.choose (fun (index, line) ->
            let m = heading.Match line

            if m.Success then
                Some(index, m.Groups["version"].Value)
            else
                None)

    // A heading above the newest release, such as `### Unreleased`, would release the previous version again.
    let otherHeading = Regex @"^###\s"

    match Array.toList headings with
    | [] -> failwith "RELEASE_NOTES.md has no release entry."
    | (start, _) :: _ when lines[.. start - 1] |> Array.exists otherHeading.IsMatch ->
        let line = lines[.. start - 1] |> Array.findIndex otherHeading.IsMatch

        failwith
            $"RELEASE_NOTES.md: the heading on line %d{line + 1} is not a release heading, such as `### 8.0.2 - 24.10.2026`."
    | (start, version) :: rest ->
        let stop =
            match rest with
            | (next, _) :: _ -> next
            | [] -> lines.Length

        let notes = lines[start + 1 .. stop - 1] |> String.concat "\n" |> _.Trim()
        version, notes

let releaseVersion = lazy (fst (latestReleaseNotes ()))

/// The package vsce writes for a version: the name in release/package.json and the version.
let vsixPath (version: string) =
    tempDir </> $"Ionide-fsharp-%s{version}.vsix"

let fable (args: string) =
    $"dotnet fable ./src/Ionide.FSharp.fsproj -s --outDir ./out %s{args}"

let clean =
    stage "clean" {
        run (fun _ ->
            Shell.cleanDirs [ tempDir; fableOutDir ]
            Shell.copyFiles releaseDir [ "README.md"; "LICENSE.md" ]
            File.Copy("RELEASE_NOTES.md", releaseDir </> "CHANGELOG.md", true))
    }

let lint =
    stage "lint" {
        run "dotnet tool restore"
        run "dotnet fantomas check src build.fsx"
    }

let restore =
    stage "restore" {
        run "dotnet tool restore"
        run "yarn install"
        run "dotnet restore src"
    }

let compile = stage "compile" { run (fable "--run webpack --mode=production") }

let copyAssets =
    stage "assets" {
        run (fun _ ->
            // remove FSAC builds of TFMs we no longer ship, an old build would win the TFM probe
            if Directory.Exists(releaseDir </> "bin") then
                for dir in Directory.GetDirectories(releaseDir </> "bin", "net*") do
                    if not (List.contains (Path.GetFileName dir) fsacTfms) then
                        printfn $"Removing stale FSAC binaries in %s{dir}"
                        Shell.deleteDir dir

            for tfm in fsacTfms do
                let fsacBin = $"packages/fsac/fsautocomplete/tools/%s{tfm}/any"

                if not (Directory.Exists fsacBin) then
                    failwith $"No FSAC binaries found for %s{tfm} in %s{fsacBin}"

                let releaseBin = releaseDir </> "bin" </> tfm
                printfn $"Copying FSAC binaries from %s{fsacBin} to %s{releaseBin}"
                Shell.cleanDir releaseBin
                Shell.copyDir releaseBin fsacBin (fun _ -> true)

            let grammarDir = "paket-files/github.com/ionide/ionide-fsgrammar/grammars"
            let syntaxesDir = releaseDir </> "syntaxes"
            Shell.cleanDir syntaxesDir

            Shell.copyFiles
                syntaxesDir
                [
                    grammarDir </> "fsharp.fsi.json"
                    grammarDir </> "fsharp.fsl.json"
                    grammarDir </> "fsharp.fsx.json"
                    grammarDir </> "fsharp.json"
                ]

            let schemasDir = releaseDir </> "schemas"
            Shell.cleanDir schemasDir
            Shell.copyFiles schemasDir [ "schemas/fableconfig.json"; "schemas/wsconfig.json" ]

            Shell.copyDir (releaseDir </> "bin" </> "x64") "lib/x64" (fun _ -> true)
            Shell.copyDir (releaseDir </> "bin" </> "x86") "lib/x86" (fun _ -> true)
            Shell.copyFiles (releaseDir </> "bin") [ "lib/libe_sqlite3.so"; "lib/libe_sqlite3.dylib" ])
    }

pipeline "Build" {
    description "Build the extension into the release folder, without the lint and the .vsix"
    workingDir __SOURCE_DIRECTORY__
    clean
    restore
    compile
    copyAssets
    runIfOnlySpecified true
}

pipeline "BuildDev" {
    description "Build the extension with DEBUG defined and an unminified bundle"
    workingDir __SOURCE_DIRECTORY__
    restore
    stage "compile" { run (fable "--define DEBUG --run webpack --mode=development") }
    runIfOnlySpecified true
}

pipeline "Watch" {
    description "Rebuild the extension on every change, with DEBUG defined"
    workingDir __SOURCE_DIRECTORY__
    restore

    stage "watch" {
        run
            "dotnet fable watch ./src/Ionide.FSharp.fsproj -s --outDir ./out --define DEBUG --runWatch webpack --mode=development"
    }

    runIfOnlySpecified true
}

// The default, and what CI runs: check the formatting, build the extension and package it as a
// .vsix in the temp folder, with the version of the newest RELEASE_NOTES.md entry.
pipeline "Package" {
    description "Check the formatting, build the extension and package it as a .vsix in the temp folder"
    workingDir __SOURCE_DIRECTORY__
    lint
    clean
    restore
    compile
    copyAssets

    stage "version" {
        run (fun _ ->
            let manifest = releaseDir </> "package.json"
            let node = JsonNode.Parse(File.ReadAllText manifest)
            node["version"] <- JsonValue.Create releaseVersion.Value
            File.WriteAllText(manifest, node.ToJsonString(JsonSerializerOptions(WriteIndented = true))))
    }

    stage "vsce" {
        workingDir releaseDir
        run $"npx @vscode/vsce package --out \"%s{tempDir}\""
    }

    runIfOnlySpecified false
}

/// Where to push the release commit and tag: the remote of the main repository, which is origin in CI.
let mainRemote (ctx: Internal.StageContext) : Async<string> =
    async {
        let! remotes = ctx.RunCommandCaptureOutput "git remote -v"

        return
            remotes
            |> Result.toOption
            |> Option.bind (fun output ->
                output.Split('\n')
                |> Array.tryFind (fun line -> line.Contains repository && line.TrimEnd().EndsWith "(push)")
                |> Option.map (fun line -> line.Split([| '\t'; ' ' |]).[0]))
            |> Option.defaultValue $"https://github.com/%s{repository}"
    }

// Publish what the Package pipeline left behind: commit and tag the version bump, create the
// GitHub release with the .vsix, and publish that same .vsix to the marketplace. This pipeline
// builds nothing. The marketplace token is read by vsce from VSCE_PAT.
pipeline "Release" {
    description "Publish the packaged extension to GitHub and the marketplace"
    workingDir __SOURCE_DIRECTORY__

    stage "git" {
        run "git add release"
        run (fun _ -> $"git commit -m \"Bump version to %s{releaseVersion.Value}\"")

        run (fun ctx ->
            async {
                let! remote = mainRemote ctx
                return $"git push %s{remote} main"
            })

        run (fun _ -> $"git tag %s{releaseVersion.Value}")

        run (fun ctx ->
            async {
                let! remote = mainRemote ctx
                return $"git push %s{remote} %s{releaseVersion.Value}"
            })
    }

    stage "github" {
        run (fun _ ->
            let version, notes = latestReleaseNotes ()
            let notesFile = tempDir </> "release-notes.md"
            File.WriteAllText(notesFile, notes)
            let prerelease = if version.Contains '-' then " --prerelease" else ""

            $"gh release create %s{version} \"%s{vsixPath version}\" --repo %s{repository} --title %s{version} --notes-file \"%s{notesFile}\"%s{prerelease}")
    }

    stage "marketplace" {
        run (fun _ -> $"npx @vscode/vsce publish --packagePath \"%s{vsixPath releaseVersion.Value}\"")
    }

    runIfOnlySpecified true
}

// Open VSX users get the C# extension from Open VSX, so point the extension pack at that one.
pipeline "OpenVsx" {
    description "Point the extension pack in release/package.json at the Open VSX C# extension"
    workingDir __SOURCE_DIRECTORY__

    stage "manifest" {
        run (fun _ ->
            let manifest = releaseDir </> "package.json"

            File.ReadAllText manifest
            |> _.Replace("ms-dotnettools.csharp", "muhammad-sammy.csharp")
            |> fun content -> File.WriteAllText(manifest, content))
    }

    runIfOnlySpecified true
}

tryPrintPipelineCommandHelp ()

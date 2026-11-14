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

/// The lines of RELEASE_NOTES.md, the line index and version of its newest release heading, and those
/// of the older ones, newest first.
/// The headings read `### 8.0.1 - 24.10.2026`, the date is optional. A `### Unreleased` section above
/// the newest release collects the entries for the next release, and is never released.
let readReleaseNotes () : string array * (int * string) * (int * string) list =
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
        |> Array.toList

    // Any other heading above the newest release, such as `### Next`, `## 8.2.0` or `###8.2.0`, would
    // release the previous version again. F# directives in code samples, such as `#r`, and a line that
    // starts with an issue number, such as `#2183`, are no heading.
    let otherHeading = Regex @"^ {0,3}(#+(\s|$)|#{2,}\d)"
    let unreleased = Regex(@"^###\s+Unreleased\s*$", RegexOptions.IgnoreCase)

    let isOtherHeading (line: string) =
        otherHeading.IsMatch line && not (unreleased.IsMatch line)

    // a new release under the heading of an old one would look released already, and publish nothing
    match headings |> List.countBy snd |> List.tryFind (fun (_, count) -> count > 1) with
    | Some(version, _) -> failwith $"RELEASE_NOTES.md: version %s{version} has more than one heading."
    | None -> ()

    match headings with
    | [] -> failwith "RELEASE_NOTES.md has no release entry."
    | (start, _) :: _ when lines[.. start - 1] |> Array.exists isOtherHeading ->
        let line = lines[.. start - 1] |> Array.findIndex isOtherHeading

        failwith
            $"RELEASE_NOTES.md: the heading on line %d{line + 1} is neither `### Unreleased` nor a release heading, such as `### 8.0.2 - 24.10.2026`."
    // its entries would end up in the notes of the newest release
    | (start, _) :: _ when lines[start + 1 ..] |> Array.exists unreleased.IsMatch ->
        failwith "RELEASE_NOTES.md: `### Unreleased` must sit above the newest release heading."
    | newest :: older -> lines, newest, older

/// The newest release entry of RELEASE_NOTES.md: its version and the notes below its heading.
let latestReleaseNotes () : string * string =
    let lines, (start, version), older = readReleaseNotes ()

    let stop =
        match older with
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
            // the changelog of the package starts at the newest release, without the Unreleased section
            let lines, (start, _), _ = readReleaseNotes ()
            File.WriteAllLines(releaseDir </> "CHANGELOG.md", lines[start..]))
    }

let lint =
    stage "lint" {
        run "dotnet tool restore"
        run "dotnet fantomas check src build.fsx"
    }

let restore =
    stage "restore" {
        run "dotnet tool restore"

        // CI installs exactly what package-lock.json says, and fails when it is out of date
        stage "npm ci" {
            whenEnvVar "CI"
            run "npm ci --no-audit --no-fund"
        }

        stage "npm install" {
            whenNot { envVar "CI" }
            run "npm install --no-audit --no-fund"
        }

        run "dotnet restore src"
    }

let compile =
    stage "compile" { run (fable "--run rolldown -c rolldown.config.mjs --minify") }

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
    stage "compile" { run (fable "--define DEBUG --run rolldown -c rolldown.config.mjs") }
    runIfOnlySpecified true
}

pipeline "Watch" {
    description "Rebuild the extension on every change, with DEBUG defined"
    workingDir __SOURCE_DIRECTORY__
    restore

    stage "watch" {
        run
            "dotnet fable watch ./src/Ionide.FSharp.fsproj -s --outDir ./out --define DEBUG --runWatch rolldown -c rolldown.config.mjs --watch"
    }

    runIfOnlySpecified true
}

// Build the extension, then start it in a downloaded VS Code and wait for FSAC to answer a hover,
// see test/smoke. On Linux without a display, run it under xvfb-run. CI runs it after Package, so
// there it tests that build instead of building again.
pipeline "Test" {
    description "Build the extension and smoke test it in VS Code"
    workingDir __SOURCE_DIRECTORY__

    stage "build" {
        whenNot { envVar "CI" }
        restore
        compile
        copyAssets
    }

    stage "smoke" { run "node test/smoke/runTest.js" }
    runIfOnlySpecified true
}

// The default, and what CI runs: check the formatting, build the extension and package it as a
// .vsix in the temp folder. On CI, the package gets the version of the newest RELEASE_NOTES.md entry.
// release/package.json keeps version 0.0.0 in git, so locally the package is version 0.0.0.
pipeline "Package" {
    description "Check the formatting, build the extension and package it as a .vsix in the temp folder"
    workingDir __SOURCE_DIRECTORY__
    lint
    clean
    restore
    compile
    copyAssets

    stage "version" {
        whenEnvVar "CI"

        run (fun _ ->
            let manifest = releaseDir </> "package.json"
            let node = JsonNode.Parse(File.ReadAllText manifest)
            node["version"] <- JsonValue.Create releaseVersion.Value
            File.WriteAllText(manifest, node.ToJsonString(JsonSerializerOptions(WriteIndented = true))))
    }

    stage "vsce" {
        workingDir releaseDir
        // release/ has its own package.json, so point npx at the root one for the vsce in package-lock.json
        run $"npx --yes=false --prefix \"%s{__SOURCE_DIRECTORY__}\" @vscode/vsce package --out \"%s{tempDir}\""
    }

    runIfOnlySpecified false
}

// Find out what a push to main releases: the version of the newest RELEASE_NOTES.md entry, unless
// it has a tag already. It fails when that version is not of the form X.Y.Z, or older than the newest
// release. On CI it writes the version to the `version` output of the step, which stays empty when
// there is nothing to release.
pipeline "Detect" {
    description "Print the version a push to main would release, if any"
    workingDir __SOURCE_DIRECTORY__

    stage "detect" {
        run (fun ctx ->
            async {
                let version = releaseVersion.Value

                // the Marketplace takes no prerelease versions, and `### 8.2.0: 24.10.2026` reads as `8.2.0:`
                if not (Regex.IsMatch(version, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")) then
                    failwith $"RELEASE_NOTES.md: version %s{version} is not of the form X.Y.Z, such as 8.0.2."

                let! output =
                    ctx.RunCommandCaptureOutput(
                        $"git ls-remote --tags https://github.com/%s{repository}",
                        disablePrintOutput = true
                    )

                // `refs/tags/8.1.0`, and `refs/tags/8.1.0^{}` for the commit of an annotated tag
                let tags =
                    match output with
                    | Error error -> failwith $"Could not list the tags of %s{repository}: %s{error}"
                    | Ok output ->
                        output.Split('\n')
                        |> Array.choose (fun line ->
                            match line.Trim().Split('\t') with
                            | [| _; ref |] when ref.StartsWith "refs/tags/" ->
                                Some(ref.Substring("refs/tags/".Length).Replace("^{}", ""))
                            | _ -> None)
                        |> Set.ofArray

                let newest =
                    tags
                    |> Seq.filter (fun tag -> Regex.IsMatch(tag, @"^\d+\.\d+\.\d+$"))
                    |> Seq.map Version.Parse
                    |> Seq.fold max (Version(0, 0, 0))

                // checked before the tag, because an older version can have a tag but no heading
                if Version.Parse version < newest then
                    failwith $"RELEASE_NOTES.md: version %s{version} is older than the newest release, %O{newest}."
                elif tags.Contains version then
                    printfn $"%s{version} is already released, nothing to do."
                else
                    printfn $"%s{version} is not released yet."

                    match Environment.GetEnvironmentVariable "GITHUB_OUTPUT" with
                    | null
                    | "" -> ()
                    | outputFile -> File.AppendAllText(outputFile, $"version=%s{version}\n")
            })
    }

    runIfOnlySpecified true
}

// Publish the .vsix the Package pipeline left behind to the VS Code Marketplace. This pipeline builds
// nothing. vsce reads the marketplace token from VSCE_PAT, and skips a version the marketplace
// already has, so a failed release can run again.
pipeline "Marketplace" {
    description "Publish the packaged extension to the VS Code Marketplace"
    workingDir __SOURCE_DIRECTORY__

    stage "marketplace" {
        run (fun _ ->
            $"npx --yes=false @vscode/vsce publish --packagePath \"%s{vsixPath releaseVersion.Value}\" --skip-duplicate")
    }

    runIfOnlySpecified true
}

// The last step of a release: tag the commit that was built, and create the GitHub release with
// the .vsix and the notes of the newest RELEASE_NOTES.md entry. The tag is what tells the Detect
// pipeline a version is released, so this runs after the extension is on both registries.
pipeline "GitHubRelease" {
    description "Tag the built commit and create the GitHub release with the packaged extension"
    workingDir __SOURCE_DIRECTORY__

    stage "github" {
        run (fun ctx ->
            async {
                let version, notes = latestReleaseNotes ()
                let notesFile = tempDir </> "release-notes.md"
                File.WriteAllText(notesFile, notes)

                let! commit = ctx.RunCommandCaptureOutput "git rev-parse HEAD"

                let commit =
                    match commit with
                    | Ok commit -> commit.Trim()
                    | Error error -> failwith $"Could not read the commit to tag: %s{error}"

                return
                    $"gh release create %s{version} \"%s{vsixPath version}\" --repo %s{repository} --target %s{commit} --title %s{version} --notes-file \"%s{notesFile}\""
            })
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

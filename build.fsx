// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#r "paket: groupref build //"
#load ".fake/build.fsx/intellisense.fsx"

open System
open System.IO
open Fake.Core
open Fake.JavaScript
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators
open Fake.Tools.Git
open Fake.Api

// --------------------------------------------------------------------------------------
// Configuration
// --------------------------------------------------------------------------------------


// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "ionide"
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "ionide-vscode-fsharp"

let fsacDir = "paket-files/github.com/fsharp/FsAutoComplete"

// Read additional information from the release notes document
let releaseNotesData =
    File.ReadAllLines "RELEASE_NOTES.md"
    |> ReleaseNotes.parseAll

let release = List.head releaseNotesData

// --------------------------------------------------------------------------------------
// Helper functions
// --------------------------------------------------------------------------------------

let run cmd args dir =
    let parms = { ExecParams.Empty with Program = cmd; WorkingDir = dir; CommandLine = args }
    if Process.shellExec parms <> 0 then
        failwithf "Error while running '%s' with args: %s" cmd args


let platformTool tool path =
    match Environment.isUnix with
    | true -> tool
    | _ ->  match ProcessUtils.tryFindFileOnPath path with
            | None -> failwithf "can't find tool %s on PATH" tool
            | Some v -> v

let npmTool =
    platformTool "npm"  "npm.cmd"

let vsceTool = lazy (platformTool "vsce" "vsce.cmd")

let runFable additionalArgs =
    let cmd = "webpack -- --config webpack.config.js " + additionalArgs
    DotNet.exec (fun p -> { p with WorkingDirectory = "src"; } ) "fable" cmd
    |> ignore

let copyFSAC releaseBin fsacBin =
    Directory.ensure releaseBin
    Shell.cleanDir releaseBin
    Shell.copyDir releaseBin fsacBin (fun _ -> true)

let copyFSACNetcore releaseBinNetcore fsacBinNetcore =
    Directory.ensure releaseBinNetcore
    Shell.cleanDir releaseBinNetcore
    Shell.copyDir releaseBinNetcore fsacBinNetcore (fun _ -> true)

let copyForge paketFilesForge releaseForge =
    Directory.ensure releaseForge
    Shell.cleanDir releaseForge
    Shell.copyDir releaseForge (sprintf "%s/temp/" paketFilesForge) (fun _ -> true)

let copyGrammar fsgrammarDir fsgrammarRelease =
    Directory.ensure fsgrammarRelease
    Shell.cleanDir fsgrammarRelease
    Shell.copyFiles fsgrammarRelease [
        fsgrammarDir </> "fsharp.fsi.json"
        fsgrammarDir </> "fsharp.fsl.json"
        fsgrammarDir </> "fsharp.fsx.json"
        fsgrammarDir </> "fsharp.json"
    ]

let copySchemas fsschemaDir fsschemaRelease =
    Directory.ensure fsschemaRelease
    Shell.cleanDir fsschemaRelease
    Shell.copyFile fsschemaRelease (fsschemaDir </> "fableconfig.json")
    Shell.copyFile fsschemaRelease (fsschemaDir </> "wsconfig.json")

let copyLib libDir releaseDir =
    Directory.ensure releaseDir
    Shell.copyDir (releaseDir </> "x64") (libDir </> "x64") (fun _ -> true)
    Shell.copyDir (releaseDir </> "x86") (libDir </> "x86") (fun _ -> true)
    Shell.copyFile releaseDir (libDir </> "libe_sqlite3.so")
    Shell.copyFile releaseDir (libDir </> "libe_sqlite3.dylib")

let buildPackage dir =
    Process.killAllByName "vsce"
    run vsceTool.Value "package" dir
    !! (sprintf "%s/*.vsix" dir)
    |> Seq.iter(Shell.moveFile "./temp/")

let setPackageJsonField name value releaseDir =
    let fileName = sprintf "./%s/package.json" releaseDir
    let lines =
        File.ReadAllLines fileName
        |> Seq.map (fun line ->
            if line.TrimStart().StartsWith(sprintf "\"%s\":" name) then
                let indent = line.Substring(0,line.IndexOf("\""))
                sprintf "%s\"%s\": %s," indent name value
            else line)
    File.WriteAllLines(fileName,lines)

let setVersion (release: ReleaseNotes.ReleaseNotes) releaseDir =
    let versionString = sprintf "\"%O\"" release.NugetVersion
    setPackageJsonField "version" versionString releaseDir

let publishToGallery releaseDir =
    let token =
        match Environment.environVarOrDefault "vsce-token" "" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> UserInput.getUserPassword "VSCE Token: "

    Process.killAllByName "vsce"
    run vsceTool.Value (sprintf "publish --pat %s" token) releaseDir

let releaseGithub (release: ReleaseNotes.ReleaseNotes) =
    let user =
        match Environment.environVarOrDefault "github-user" "" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> UserInput.getUserInput "Username: "
    let pw =
        match Environment.environVarOrDefault "github-pw" "" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> UserInput.getUserPassword "Password: "
    let remote =
        CommandHelper.getGitResult "" "remote -v"
        |> Seq.filter (fun (s: string) -> s.EndsWith("(push)"))
        |> Seq.tryFind (fun (s: string) -> s.Contains(gitOwner + "/" + gitName))
        |> function None -> gitHome + "/" + gitName | Some (s: string) -> s.Split().[0]

    Staging.stageAll ""
    Commit.exec "" (sprintf "Bump version to %s" release.NugetVersion)
    Branches.pushBranch "" remote (Information.getBranchName "")

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" remote release.NugetVersion

    let files = !! ("./temp" </> "*.vsix")

    // release on github
    let cl =
        GitHub.createClient user pw
        |> GitHub.draftNewRelease gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes

    (cl,files)
    ||> Seq.fold (fun acc e -> acc |> GitHub.uploadFile e)
    |> GitHub.publishDraft//releaseDraft
    |> Async.RunSynchronously

// --------------------------------------------------------------------------------------
// Target definitions
// --------------------------------------------------------------------------------------
Target.initEnvironment ()

Target.create "Clean" (fun _ ->
    Shell.cleanDir "./temp"
    Shell.copyFiles "release" ["README.md"; "LICENSE.md"]
    Shell.copyFile "release/CHANGELOG.md" "RELEASE_NOTES.md"
)

Target.create "YarnInstall" <| fun _ ->
    Yarn.install id

Target.create "DotNetRestore" <| fun _ ->
    DotNet.restore id "src"

Target.create "Watch" (fun _ ->
    runFable "--watch"
)

Target.create "InstallVSCE" ( fun _ ->
    Process.killAllByName  "npm"
    run npmTool "install -g vsce" ""
)

Target.create "CopyDocs" (fun _ ->
    Shell.copyFiles "release" ["README.md"; "LICENSE.md"]
    Shell.copyFile "release/CHANGELOG.md" "RELEASE_NOTES.md"
)

Target.create "RunScript" (fun _ ->
    // Ideally we would want a production (minized) build but UglifyJS fail on PerMessageDeflate.js as it contains non-ES6 javascript.
    runFable ""
)

Target.create "CopyFSAC" (fun _ ->
    let fsacBin = sprintf "%s/bin/release" fsacDir
    let releaseBin = "release/bin"
    copyFSAC releaseBin fsacBin
)

Target.create "CopyFSACNetcore" (fun _ ->
    let fsacBinNetcore = sprintf "%s/bin/release_netcore" fsacDir
    let releaseBinNetcore = "release/bin_netcore"

    copyFSACNetcore releaseBinNetcore fsacBinNetcore
)

Target.create "CopyForge" (fun _ ->
    let forgeDir = "paket-files/github.com/ionide/Forge"
    let releaseForge = "release/bin_forge"

    copyForge forgeDir releaseForge
)

Target.create "CopyGrammar" (fun _ ->
    let fsgrammarDir = "paket-files/github.com/ionide/ionide-fsgrammar/grammar"
    let fsgrammarRelease = "release/syntaxes"

    copyGrammar fsgrammarDir fsgrammarRelease
)

Target.create "CopySchemas" (fun _ ->
    let fsschemaDir = "schemas"
    let fsschemaRelease = "release/schemas"

    copySchemas fsschemaDir fsschemaRelease
)

Target.create "CopyLib" (fun _ ->
    let libDir = "lib"
    let releaseDir = "release/bin"

    copyLib libDir releaseDir
)

Target.create "BuildPackage" ( fun _ ->
    buildPackage "release"
)

Target.create "SetVersion" (fun _ ->
    setVersion release "release"
)

Target.create "PublishToGallery" ( fun _ ->
    publishToGallery "release"
)

Target.create "ReleaseGitHub" (fun _ ->
    releaseGithub release
)

// --------------------------------------------------------------------------------------
// Run build by default. Invoke 'build <Target>' to override
// --------------------------------------------------------------------------------------

Target.create "Default" ignore
Target.create "Build" ignore
Target.create "BuildExp" ignore
Target.create "Release" ignore
Target.create "ReleaseExp" ignore
Target.create "BuildPackages" ignore

"YarnInstall" ==> "RunScript"
"DotNetRestore" ==> "RunScript"

"Clean"
==> "RunScript"
==> "Default"

"Clean"
==> "RunScript"
==> "CopyDocs"
==> "CopyFSAC"
==> "CopyFSACNetcore"
==> "CopyForge"
==> "CopyGrammar"
==> "CopySchemas"
==> "CopyLib"
==> "Build"


"YarnInstall" ==> "Build"
"DotNetRestore" ==> "Build"

"Build"
==> "SetVersion"
==> "BuildPackage"
==> "ReleaseGitHub"
==> "PublishToGallery"
==> "Release"

Target.runOrDefault "Default"

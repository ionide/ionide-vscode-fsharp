// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#I "packages/build/FAKE/tools"
#r "FakeLib.dll"
open System
open System.Diagnostics
open System.IO
open Fake
open Fake.Git
open Fake.ProcessHelper
open Fake.ReleaseNotesHelper
open Fake.YarnHelper
open Fake.ZipHelper


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
        failwithf "Error while running '%s' with args: %s" cmd args


let platformTool tool path =
    match isUnix with
    | true -> tool
    | _ ->  match ProcessHelper.tryFindFileOnPath path with
            | None -> failwithf "can't find tool %s on PATH" tool
            | Some v -> v

let npmTool =
    platformTool "npm"  "npm.cmd"

let vsceTool = lazy (platformTool "vsce" "vsce.cmd")


let releaseBin      = "release/bin"
let fsacBin         = "paket-files/github.com/fsharp/FsAutoComplete/bin/release"


// --------------------------------------------------------------------------------------
// Build the Generator project and run it
// --------------------------------------------------------------------------------------

Target "Clean" (fun _ ->
    CleanDir "./temp"
    CopyFiles "release" ["README.md"; "LICENSE.md"]
    CopyFile "release/CHANGELOG.md" "RELEASE_NOTES.md"
)

Target "YarnInstall" <| fun () ->
    Yarn (fun p -> { p with Command = Install Standard })

Target "DotNetRestore" <| fun () ->
    DotNetCli.Restore (fun p -> { p with WorkingDir = "src" } )

let runFable additionalArgs noTimeout =
    let cmd = "fable webpack -- --config webpack.config.js " + additionalArgs
    let timeout = if noTimeout then TimeSpan.MaxValue else TimeSpan.FromMinutes 30.
    DotNetCli.RunCommand (fun p -> { p with WorkingDir = "src"; TimeOut = timeout } ) cmd

Target "RunScript" (fun _ ->
    // Ideally we would want a production (minized) build but UglifyJS fail on PerMessageDeflate.js as it contains non-ES6 javascript.
    runFable "" false
)

Target "Watch" (fun _ ->
    runFable "--watch" true
)

Target "CopyFSAC" (fun _ ->
    ensureDirectory releaseBin
    CleanDir releaseBin

    !! (fsacBin + "/*")
    |> CopyFiles releaseBin
)

let releaseForge = "release/bin_forge"
let releaseBinForge = "release/bin_forge/bin"

let forgeBin = "paket-files/github.com/fsharp-editing/Forge/temp/Bin/*.dll"
let forgeExe = "paket-files/github.com/fsharp-editing/Forge/temp/Forge.exe"
let forgeConfig = "paket-files/github.com/fsharp-editing/Forge/temp/Forge.exe.config"


Target "CopyForge" (fun _ ->
    ensureDirectory releaseBinForge
    ensureDirectory releaseForge

    CleanDir releaseBinForge
    checkFileExists forgeExe
    !! forgeExe
    ++ forgeConfig
    |> CopyFiles releaseForge

    !! forgeBin
    |> CopyFiles releaseBinForge


)

let fsgrammarDir = "paket-files/github.com/ionide/ionide-fsgrammar/grammar"
let fsgrammarRelease = "release/syntaxes"


Target "CopyGrammar" (fun _ ->
    ensureDirectory fsgrammarRelease
    CleanDir fsgrammarRelease
    CopyFiles fsgrammarRelease [
        fsgrammarDir </> "fsharp.fsi.json"
        fsgrammarDir </> "fsharp.fsl.json"
        fsgrammarDir </> "fsharp.fsx.json"
        fsgrammarDir </> "fsharp.json"
    ]
)


let fsschemaDir = "schemas"

let fsschemaRelease = "release/schemas"
Target "CopySchemas" (fun _ ->
    ensureDirectory fsschemaRelease
    CleanDir fsschemaRelease
    CopyFile fsschemaRelease (fsschemaDir </> "fableconfig.json")
)


Target "InstallVSCE" ( fun _ ->
    killProcess "npm"
    run npmTool "install -g vsce" ""
)

Target "SetVersion" (fun _ ->
    let fileName = "./release/package.json"
    let lines =
        File.ReadAllLines fileName
        |> Seq.map (fun line ->
            if line.TrimStart().StartsWith("\"version\":") then
                let indent = line.Substring(0,line.IndexOf("\""))
                sprintf "%s\"version\": \"%O\"," indent release.NugetVersion
            else line)
    File.WriteAllLines(fileName,lines)
)

Target "BuildPackage" ( fun _ ->
    killProcess "vsce"
    run vsceTool.Value "package" "release"
    !! "release/*.vsix"
    |> Seq.iter(MoveFile "./temp/")
)


Target "PublishToGallery" ( fun _ ->
    let token =
        match getBuildParam "vsce-token" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserPassword "VSCE Token: "

    killProcess "vsce"
    run vsceTool.Value (sprintf "publish --pat %s" token) "release"
)

#load "paket-files/build/fsharp/FAKE/modules/Octokit/Octokit.fsx"
open Octokit



Target "ReleaseGitHub" (fun _ ->
    let user =
        match getBuildParam "github-user" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserInput "Username: "
    let pw =
        match getBuildParam "github-pw" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserPassword "Password: "
    let remote =
        Git.CommandHelper.getGitResult "" "remote -v"
        |> Seq.filter (fun (s: string) -> s.EndsWith("(push)"))
        |> Seq.tryFind (fun (s: string) -> s.Contains(gitOwner + "/" + gitName))
        |> function None -> gitHome + "/" + gitName | Some (s: string) -> s.Split().[0]

    StageAll ""
    Git.Commit.Commit "" (sprintf "Bump version to %s" release.NugetVersion)
    Branches.pushBranch "" remote (Information.getBranchName "")

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" remote release.NugetVersion

    let file = !! ("./temp" </> "*.vsix") |> Seq.head

    // release on github
    createClient user pw
    |> createDraft gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes
    |> uploadFile file
    |> releaseDraft
    |> Async.RunSynchronously
)

// --------------------------------------------------------------------------------------
// Run generator by default. Invoke 'build <Target>' to override
// --------------------------------------------------------------------------------------

Target "Default" DoNothing
Target "Build" DoNothing
Target "Release" DoNothing

"YarnInstall" ?=> "RunScript"
"DotNetRestore" ?=> "RunScript"

"Clean"
==> "RunScript"
==> "Default"

"Clean"
==> "RunScript"
==> "CopyFSAC"
==> "CopyForge"
==> "CopyGrammar"
==> "CopySchemas"
==> "Build"

"YarnInstall" ==> "Build"
"DotNetRestore" ==> "Build"

"Build"
==> "SetVersion"
// ==> "InstallVSCE"
==> "BuildPackage"
==> "ReleaseGitHub"
==> "PublishToGallery"
==> "Release"

RunTargetOrDefault "Default"

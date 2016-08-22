// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#I "packages/FAKE/tools"
#r "FakeLib.dll"
open System
open System.Diagnostics
open System.IO
open Fake
open Fake.Git
open Fake.ProcessHelper
open Fake.ReleaseNotesHelper
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
    isUnix |> function | true -> tool | _ -> path

let npmTool =
    platformTool "npm" ("packages" </> "Npm.js" </> "tools"  </> "npm.cmd" |> FullName)

let vsceTool =
    platformTool "vsce" ("packages" </> "Node.js" </> "vsce.cmd" |> FullName)

let codeTool =
    platformTool "code" (ProgramFilesX86  </> "Microsoft VS Code" </> "bin/code.cmd")


let releaseTestsBin = "release_test/bin"
let releaseBin      = "release/bin"
let fsacBin         = "paket-files/github.com/ionide/FsAutoComplete/bin/release"


// --------------------------------------------------------------------------------------
// Build the Generator project and run it
// --------------------------------------------------------------------------------------

Target "Clean" (fun _ ->
    CleanDir "./temp"
    CopyFiles "release" ["README.md"; "LICENSE.md"; "RELEASE_NOTES.md"]
)

Target "RunScript" (fun _ ->
    run npmTool "install" "release"
    run npmTool "run build" "release"
)

Target "CopyFSACToTests" (fun _ ->
    ensureDirectory releaseTestsBin
    CleanDir releaseTestsBin

    !! (fsacBin + "/*")
    |> CopyFiles releaseTestsBin
)

Target "BuildTest" (fun _ ->
    run npmTool "install" "test"
    run npmTool "run build" "test"
)

Target "RunTest" (fun _ ->
    run npmTool "install" "release_test"
    run npmTool "run test" "release_test"
)



Target "CopyFSAC" (fun _ ->
    ensureDirectory releaseBin
    CleanDir releaseBin

    !! (fsacBin + "/*")
    |> CopyFiles releaseBin
)

let releaseBinForge = "release/bin_forge"
let forgeBin = "paket-files/github.com/fsprojects/Forge/temp/bin"
let forgeExe = forgeBin </> "Forge.exe"
let posixDll = forgeBin </> "Mono.Posix.dll"

Target "CopyForge" (fun _ ->
    ensureDirectory releaseBinForge
    CleanDir releaseBinForge
    checkFileExists forgeExe
    checkFileExists posixDll
    !! forgeExe
    ++ posixDll
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
    run vsceTool "package" "release"
    !! "release/*.vsix"
    |> Seq.iter(MoveFile "./temp/")
)

Target "TryPackage"(fun _ ->
    killProcess "code"
    run codeTool (sprintf "./temp/Ionide-fsharp-%s.vsix" release.NugetVersion) ""
)


Target "PublishToGallery" ( fun _ ->
    let token =
        match getBuildParam "vsce-token" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserPassword "VSCE Token: "

    killProcess "vsce"
    run vsceTool (sprintf "publish --pat %s" token) "release"
)

#load "paket-files/fsharp/FAKE/modules/Octokit/Octokit.fsx"
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
Target "Test" DoNothing

"Clean"
==> "RunScript"
==> "Default"

"CopyFSACToTests"
==> "BuildTest"
==> "RunTest"
==> "Test"
//==> "BuildPackage"

"Clean"
==> "RunScript"
==> "CopyFSAC"
==> "CopyForge"
==> "CopyGrammar"
==> "Build"

"Build"
==> "SetVersion"
==> "InstallVSCE"
==> "BuildPackage"
==> "ReleaseGitHub"
==> "PublishToGallery"
==> "Release"


"BuildPackage"
==> "TryPackage"

RunTargetOrDefault "Default"

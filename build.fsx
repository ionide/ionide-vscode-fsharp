// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------
#if DOTNETCORE

#r "paket:
source nuget/dotnetcore
source https://api.nuget.org/v3/index.json
nuget Fake.Core.Target
nuget Fake.Core.UserInput
nuget Fake.IO.FileSystem
nuget Fake.IO.Zip
nuget Fake.JavaScript.Yarn
nuget Fake.Core.ReleaseNotes
nuget Fake.DotNet.Cli
nuget Fake.Tools.Git
nuget Octokit //"
#endif

#if DOTNETCORE
// We need to use this for now as "regular" Fake breaks when its caching logic cannot find "intellisense.fsx".
// This is the reason why we need to checkin the "intellisense.fsx" file for now...
#load ".fake/build.fsx/intellisense.fsx"

#else

#I "packages/build/FAKE/tools"
#r "FakeLib.dll"
#r "packages/build/Octokit/lib/net45/Octokit.dll"

#endif

open System
open System.IO
open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.JavaScript
open Fake.Tools


// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "ionide"
let gitHome = "https://github.com/" + gitOwner


// The name of the project on GitHub
let gitName = "ionide-vscode-fsharp"

// The url for the raw files hosted
let gitRaw = Environment.environVarOrDefault "gitRaw" "https://raw.github.com/ionide"


// Read additional information from the release notes document
let releaseNotesData =
    File.ReadAllLines "RELEASE_NOTES.md"
    |> ReleaseNotes.parseAll

let release = List.head releaseNotesData

let msg =  release.Notes |> List.fold (fun r s -> r + s + "\n") ""
let releaseMsg = (sprintf "Release %s\n" release.NugetVersion) + msg

let run cmd args dir =
    if Process.execSimple( fun info -> 
        { info with 
            FileName = cmd
            WorkingDirectory = if not( String.IsNullOrWhiteSpace dir) then
                                dir 
                                    else 
                                ""
            Arguments = args 
        }
    ) System.TimeSpan.MaxValue <> 0 then
        failwithf "Error while running '%s' with args: %s" cmd args


let platformTool tool path =
    match Environment.isUnix with
    | true -> tool
    | _ ->  match Process.tryFindFileOnPath path with
            | None -> failwithf "can't find tool %s on PATH" tool
            | Some v -> v

let npmTool =
    platformTool "npm"  "npm.cmd"

let vsceTool = lazy (platformTool "vsce" "vsce.cmd")


let releaseBin      = "release/bin"
let fsacBin         = "paket-files/github.com/fsharp/FsAutoComplete/bin/release"

let releaseBinNetcore = releaseBin + "_netcore"
let fsacBinNetcore = fsacBin + "_netcore"

// --------------------------------------------------------------------------------------
// Build the Generator project and run it
// --------------------------------------------------------------------------------------

Target.create "Clean" (fun _ ->
    Shell.cleanDir "./temp"
    Shell.copyFiles "release" ["README.md"; "LICENSE.md"]
    Shell.copyFile "release/CHANGELOG.md" "RELEASE_NOTES.md"
)

Target.create "YarnInstall" (fun _ ->
    Yarn.install (fun _ -> Yarn.defaultYarnParams))

Target.create "DotNetRestore" (fun _ ->
    let options = DotNet.Options.Create ()
    DotNet.restore (fun p -> { p with Common = { options with WorkingDirectory = "src"} } ) |> ignore
)

let runFable additionalArgs noTimeout =
    let cmd = "fable webpack -- --config webpack.config.js"
    //TODO: Remove timeout? Not available any more in DotNet exec
    let timeout = if noTimeout then TimeSpan.MaxValue else TimeSpan.FromMinutes 30.
    DotNet.exec (fun p -> { p with WorkingDirectory = "src"; } ) cmd additionalArgs

Target.create "RunScript" (fun _ ->
    // Ideally we would want a production (minized) build but UglifyJS fail on PerMessageDeflate.js as it contains non-ES6 javascript.
    runFable "" false |> ignore
)

Target.create "Watch" (fun _ ->
    runFable "--watch" true |> ignore
)

Target.create "CopyFSAC" (fun _ ->
    Directory.ensure releaseBin
    Shell.cleanDir releaseBin

    !! (fsacBin + "/*")
    |> Shell.copyFiles releaseBin
)

Target.create "CopyFSACNetcore" (fun _ ->
    Directory.ensure releaseBinNetcore
    Shell.cleanDir releaseBinNetcore

    Shell.copyDir releaseBinNetcore fsacBinNetcore (fun _ -> true)

    let mainfestFile = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <assemblyIdentity version="1.0.0.0" name="MyApplication.app"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false"/>
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>"""

    System.IO.File.WriteAllText(releaseBinNetcore </> "default.win32manifest", mainfestFile)
)

let releaseForge = "release/bin_forge"
let releaseBinForge = "release/bin_forge/bin"

let forgeBin = "paket-files/github.com/fsharp-editing/Forge/temp/Bin/*.dll"
let forgeExe = "paket-files/github.com/fsharp-editing/Forge/temp/Forge.exe"
let forgeConfig = "paket-files/github.com/fsharp-editing/Forge/temp/Forge.exe.config"


Target.create "CopyForge" (fun _ ->
    Directory.ensure releaseBinForge
    Directory.ensure releaseForge

    Shell.cleanDir releaseBinForge
    File.checkExists forgeExe
    !! forgeExe
    ++ forgeConfig
    |> Shell.copyFiles releaseForge

    !! forgeBin
    |> Shell.copyFiles releaseBinForge


)

let fsgrammarDir = "paket-files/github.com/ionide/ionide-fsgrammar/grammar"
let fsgrammarRelease = "release/syntaxes"


Target.create "CopyGrammar" (fun _ ->
    Directory.ensure fsgrammarRelease
    Shell.cleanDir fsgrammarRelease
    Shell.copyFiles fsgrammarRelease [
        fsgrammarDir </> "fsharp.fsi.json"
        fsgrammarDir </> "fsharp.fsl.json"
        fsgrammarDir </> "fsharp.fsx.json"
        fsgrammarDir </> "fsharp.json"
    ]
)


let fsschemaDir = "schemas"

let fsschemaRelease = "release/schemas"
Target.create "CopySchemas" (fun _ ->
    Directory.ensure fsschemaRelease
    Shell.cleanDir fsschemaRelease
    Shell.copyFile fsschemaRelease (fsschemaDir </> "fableconfig.json")
    Shell.copyFile fsschemaRelease (fsschemaDir </> "wsconfig.json")
)


Target.create "InstallVSCE" ( fun _ ->
    Process.killAllByName "npm"
    run npmTool "install -g vsce" ""
)

Target.create "SetVersion" (fun _ ->
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

Target.create "BuildPackage" ( fun _ ->
    Process.killAllByName "vsce"
    run vsceTool.Value "package" "release"
    !! "release/*.vsix"
    |> Seq.iter(Shell.moveFile "./temp/")
)


Target.create "PublishToGallery" ( fun _ ->
    let token =
        //TODO: replaced with Environment.GetEnvironmentVariable as it seemed to be the best match
        match Environment.GetEnvironmentVariable "vsce-token" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> UserInput.getUserPassword "VSCE Token: "

    Process.killAllByName "vsce"
    run vsceTool.Value (sprintf "publish --pat %s" token) "release"
)

#load "paket-files/build/fsharp/FAKE/modules/Octokit/Octokit.fsx"
open Octokit



Target.create "ReleaseGitHub" (fun _ ->
    let user =
        //TODO: replaced with Environment.GetEnvironmentVariable as it seemed to be the best match
        match Environment.GetEnvironmentVariable "github-user" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> UserInput.getUserInput "Username: "
    let pw =
        //TODO: replaced with Environment.GetEnvironmentVariable as it seemed to be the best match
        match Environment.GetEnvironmentVariable "github-pw" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> UserInput.getUserPassword "Password: "
    let remote =
        Git.CommandHelper.getGitResult "" "remote -v"
        |> Seq.filter (fun (s: string) -> s.EndsWith("(push)"))
        |> Seq.tryFind (fun (s: string) -> s.Contains(gitOwner + "/" + gitName))
        |> function None -> gitHome + "/" + gitName | Some (s: string) -> s.Split().[0]

    Git.Staging.stageAll ""
    Git.Commit.exec "" (sprintf "Bump version to %s" release.NugetVersion)
    Git.Branches.pushBranch "" remote (Git.Information.getBranchName "")

    Git.Branches.tag "" release.NugetVersion
    Git.Branches.pushTag "" remote release.NugetVersion

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

Target.create "Default" ignore
Target.create "Build" ignore
Target.create "Release" ignore

"YarnInstall" ?=> "RunScript"
"DotNetRestore" ?=> "RunScript"

"Clean"
==> "RunScript"
==> "Default"

"Clean"
==> "RunScript"
==> "CopyFSAC"
==> "CopyFSACNetcore"
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

Target.runOrDefault "Default"

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
#load "src/Core/DTO.fs"
#load "src/Core/LanguageService.fs"
#load "src/Components/Linter.fs"
#load "src/Components/Tooltip.fs"
#load "src/Components/Autocomplete.fs"
#load "src/Components/ParameterHints.fs"
#load "src/Components/Definition.fs"
#load "src/Components/References.fs"
#load "src/Components/Outline.fs"
#load "src/Components/Fsi.fs"
#load "src/Components/QuickInfo.fs"
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

let npmTool =
    match isUnix with
    | true -> "/usr/local/bin/npm"
    | _ -> __SOURCE_DIRECTORY__ </> "packages/Npm.js/tools/npm.cmd"
    
let vsceTool =
    #if MONO
        "vsce"
    #else
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) </> "npm" </> "vsce.cmd"
    #endif
    

// --------------------------------------------------------------------------------------
// Build the Generator project and run it
// --------------------------------------------------------------------------------------

Target "Clean" (fun _ ->
    CleanDir "./temp"
    CopyFiles "release" ["README.md"; "LICENSE.md"; "RELEASE_NOTES.md"]
)

#if MONO
Target "BuildGenerator" (fun () ->
    [ __SOURCE_DIRECTORY__ </> "src" </> "Ionide.FSharp.fsproj" ]
    |> MSBuildDebug "" "Rebuild"
    |> Log "AppBuild-Output: "
)

Target "RunGenerator" (fun () ->
    (TimeSpan.FromMinutes 5.0)
    |> ProcessHelper.ExecProcess (fun p ->
        p.FileName <- __SOURCE_DIRECTORY__ </> "src" </> "bin" </> "Debug" </> "Ionide.FSharp.exe" )
    |> ignore
)
#else
Target "RunScript" (fun () ->
    Ionide.VSCode.Generator.translateModules typeof<Ionide.VSCode.FSharp> (".." </> "release" </> "fsharp.js")
)
#endif

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

Target "LoginToVSCE" ( fun _ ->
    let publisher =
        match getBuildParam "vsce-publisher" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserPassword "VSCE Publisher: "
        
    let token =
        match getBuildParam "vsce-token" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserPassword "VSCE Token: "
        
    killProcess "vsce"
    run vsceTool (sprintf "login %s -pat %s" publisher token) "release"
)

Target "PublishToGallery" ( fun _ -> 
    killProcess "vsce"
    run vsceTool "publish" "release"
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
Target "Release" DoNothing

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

"Default"
  ==> "SetVersion"
  ==> "InstallVSCE"
  ==> "BuildPackage"
  ==> "ReleaseGitHub"
  // ==> "LoginToVSCE"
  ==> "PublishToGallery"
  ==> "Release"
RunTargetOrDefault "Default"

open Fake.IO
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

#nowarn "44"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "ionide"
let gitHome = "https://github.com/" + gitOwner


// The name of the project on GitHub
let gitName = "ionide-vscode-fsharp"

// The url for the raw files hosted
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/ionide"


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


// --------------------------------------------------------------------------------------
// Build the Generator project and run it
// --------------------------------------------------------------------------------------

Target "Clean" (fun _ ->
    CleanDir "./temp"
    CopyFiles "release" ["README.md"; "LICENSE.md"]
    CopyFile "release/CHANGELOG.md" "RELEASE_NOTES.md"

    CleanDir "./release-exp"
)

Target "YarnInstall" <| fun () ->
    Yarn (fun p -> { p with Command = Install Standard })

Target "DotNetRestore" <| fun () ->
    DotNetCli.Restore (fun p -> { p with WorkingDir = "src" } )

let runFable additionalArgs noTimeout =
    let cmd = "fable webpack -- --config webpack.config.js " + additionalArgs
    let timeout = if noTimeout then TimeSpan.MaxValue else TimeSpan.FromMinutes 30.
    DotNetCli.RunCommand (fun p -> { p with WorkingDir = "src"; TimeOut = timeout } ) cmd

Target "Watch" (fun _ ->
    runFable "--watch" true
)

let copyFSAC releaseBin fsacBin =
    ensureDirectory releaseBin
    CleanDir releaseBin

    CopyDir releaseBin fsacBin (fun _ -> true)

let copyFSACNetcore releaseBinNetcore fsacBinNetcore =
    ensureDirectory releaseBinNetcore
    CleanDir releaseBinNetcore

    CopyDir releaseBinNetcore fsacBinNetcore (fun _ -> true)

let copyForge paketFilesForge releaseForge =

    let releaseBinForge = sprintf "%s/bin" releaseForge

    let forgeBin = sprintf "%s/temp/Bin/*.dll" paketFilesForge
    let forgeExe = sprintf "%s/temp/Forge.exe" paketFilesForge
    let forgeConfig = sprintf "%s/temp/Forge.exe.config" paketFilesForge

    ensureDirectory releaseBinForge
    ensureDirectory releaseForge

    CleanDir releaseBinForge
    checkFileExists forgeExe
    !! forgeExe
    ++ forgeConfig
    |> CopyFiles releaseForge

    !! forgeBin
    |> CopyFiles releaseBinForge

let copyGrammar fsgrammarDir fsgrammarRelease =
    ensureDirectory fsgrammarRelease
    CleanDir fsgrammarRelease
    CopyFiles fsgrammarRelease [
        fsgrammarDir </> "fsharp.fsi.json"
        fsgrammarDir </> "fsharp.fsl.json"
        fsgrammarDir </> "fsharp.fsx.json"
        fsgrammarDir </> "fsharp.json"
    ]

let copySchemas fsschemaDir fsschemaRelease =
    ensureDirectory fsschemaRelease
    CleanDir fsschemaRelease
    CopyFile fsschemaRelease (fsschemaDir </> "fableconfig.json")
    CopyFile fsschemaRelease (fsschemaDir </> "wsconfig.json")

let buildPackage dir =
    killProcess "vsce"
    run vsceTool.Value "package" dir
    !! (sprintf "%s/*.vsix" dir)
    |> Seq.iter(MoveFile "./temp/")

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

let setVersion (release: ReleaseNotes) releaseDir =
    setPackageJsonField "version" (sprintf "\"%O\"" release.NugetVersion) releaseDir

let publishToGallery releaseDir =
    let token =
        match getBuildParam "vsce-token" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserPassword "VSCE Token: "

    killProcess "vsce"
    run vsceTool.Value (sprintf "publish --pat %s" token) releaseDir

#load "paket-files/build/fsharp/FAKE/modules/Octokit/Octokit.fsx"
open Octokit

let releaseGithub (release: ReleaseNotes) =
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

Target "InstallVSCE" ( fun _ ->
    killProcess "npm"
    run npmTool "install -g vsce" ""
)

module StableExtension =

    let ionideExtensionGuid = "0ea05e3f-5a38-419d-8305-f6c3f6d409d2"

    Target "CopyDocs" (fun _ ->
        CopyFiles "release" ["README.md"; "LICENSE.md"]
        CopyFile "release/CHANGELOG.md" "RELEASE_NOTES.md"
    )

    Target "RunScript" (fun _ ->
        // Ideally we would want a production (minized) build but UglifyJS fail on PerMessageDeflate.js as it contains non-ES6 javascript.
        runFable "" false
    )

    let fsacDir = "paket-files/github.com/fsharp/FsAutoComplete"

    Target "CopyFSAC" (fun _ ->
        let fsacBin = sprintf "%s/bin/release" fsacDir
        let releaseBin = "release/bin"
        copyFSAC releaseBin fsacBin
    )

    Target "CopyFSACNetcore" (fun _ ->
        let fsacBinNetcore = sprintf "%s/bin/release_netcore" fsacDir
        let releaseBinNetcore = "release/bin_netcore"

        copyFSACNetcore releaseBinNetcore fsacBinNetcore
    )

    Target "CopyForge" (fun _ ->
        let forgeDir = "paket-files/github.com/fsharp-editing/Forge"
        let releaseForge = "release/bin_forge"

        copyForge forgeDir releaseForge
    )

    Target "CopyGrammar" (fun _ ->
        let fsgrammarDir = "paket-files/github.com/ionide/ionide-fsgrammar/grammar"
        let fsgrammarRelease = "release/syntaxes"

        copyGrammar fsgrammarDir fsgrammarRelease
    )

    Target "CopySchemas" (fun _ ->
        let fsschemaDir = "schemas"
        let fsschemaRelease = "release/schemas"

        copySchemas fsschemaDir fsschemaRelease
    )

    Target "BuildPackage" ( fun _ ->
        buildPackage "release"
    )

    // Read additional information from the release notes document
    let releaseNotesData =
        File.ReadAllLines "RELEASE_NOTES.md"
        |> parseAllReleaseNotes

    let release = List.head releaseNotesData

    let msg =  release.Notes |> List.fold (fun r s -> r + s + "\n") ""
    let releaseMsg = (sprintf "Release %s\n" release.NugetVersion) + msg

    Target "SetVersion" (fun _ ->
        setVersion release "release"
    )

    Target "PublishToGallery" ( fun _ ->
        publishToGallery "release"
    )

    Target "ReleaseGitHub" (fun _ ->
        releaseGithub release
    )

module ExperimentalExtension =

    let releaseExpDir = "release-exp"
    let experimentalExtensionId = "experimental-fsharp"
    let experimentalExtensionGuid = "e55f9a16-11f8-4e9c-854c-5fb440534340"

    Target "ExpRunScript" (fun _ ->
        // Ideally we would want a production (minized) build but UglifyJS fail on PerMessageDeflate.js as it contains non-ES6 javascript.
        runFable "--env.ionideExperimental" false
    )

    Target "ExpCopyAssets" (fun _ ->
        ensureDirectory releaseExpDir

        [ "release/package.json"
          "release/language-configuration.json" ]
        |> CopyFiles releaseExpDir

        CopyDir (sprintf "%s/images" releaseExpDir) "release/images" (fun _ -> true)
    )

    Target "ExpCopyDocs" (fun _ ->
        CopyFiles releaseExpDir ["LICENSE.md"]
        CopyFile (sprintf "%s/README.md" releaseExpDir) "README_EXPERIMENTAL.md"
        CopyFile (sprintf "%s/CHANGELOG.md" releaseExpDir) "RELEASE_NOTES_EXPERIMENTAL.md"
    )

    Target "ExpUpdatePackageId" (fun _ ->
        let dir = releaseExpDir

        // replace "name": "Ionide-fsharp" with "experimental-fsharp"
        let fileName = Path.Combine(dir, "package.json")

        let capitalize (s: string) = sprintf "%c%s" (s.[0] |> Char.ToUpper) (s.Substring(1))

        fileName
        |> File.ReadAllText
        |> fun text -> text.Replace(capitalize "ionide-fsharp", experimentalExtensionId) // case sensitive is the only occurrence
        |> fun text -> text.Replace("ionide-fsharp", experimentalExtensionId) // case sensitive is the only two occurrence
        |> fun text -> text.Replace(StableExtension.ionideExtensionGuid, experimentalExtensionGuid) // replace guid of extension
        |> fun text -> File.WriteAllText(fileName, text)

        // set in preview
        setPackageJsonField "preview" "true" dir
    )

    Target "ExpCopyFSAC" (fun _ ->
        let vendorDirPath = Path.Combine(__SOURCE_DIRECTORY__, "vendor") |> Path.GetFullPath
        let fsacDirPath = Path.Combine(vendorDirPath, "paket-files", "github.com", "fsharp", "FsAutoComplete")

        // restore git repo
        run (Path.GetFullPath "paket.exe") "restore" vendorDirPath

        // get vnext tag
        run "git" "tag -l vnext" fsacDirPath

        // checkout vnext or fail
        run "git" "checkout vnext" fsacDirPath

        // local release it
        run (if isUnix then "build.sh" else "build.cmd") "LocalRelease" fsacDirPath

        // copy to out dir

        let fsacDir = "vendor/paket-files/github.com/fsharp/FsAutoComplete"

        let releaseBin = sprintf "%s/bin" releaseExpDir
        let fsacBin = sprintf "%s/bin/release" fsacDir

        let releaseBinNetcore = sprintf "%s/bin_netcore" releaseExpDir
        let fsacBinNetcore = sprintf "%s/bin/release" fsacDir

        ensureDirectory releaseBin
        CleanDir releaseBin
        copyFSAC releaseBin fsacBin

        ensureDirectory releaseBinNetcore
        CleanDir releaseBinNetcore
        copyFSACNetcore releaseBinNetcore fsacBinNetcore
    )

    Target "ExpCopyForge" (fun _ ->
        let forgeDir = "paket-files/github.com/fsharp-editing/Forge"
        let releaseForge = sprintf "%s/bin_forge" releaseExpDir

        copyForge forgeDir releaseForge
    )

    Target "ExpCopyGrammar" (fun _ ->
        let fsgrammarDir = "paket-files/github.com/ionide/ionide-fsgrammar/grammar"
        let fsgrammarRelease = sprintf "%s/syntaxes" releaseExpDir

        copyGrammar fsgrammarDir fsgrammarRelease
    )

    Target "ExpCopySchemas" (fun _ ->
        let fsschemaDir = "schemas"
        let fsschemaRelease = sprintf "%s/schemas" releaseExpDir

        copySchemas fsschemaDir fsschemaRelease
    )

    Target "BuildPackageExp" ( fun _ ->
        buildPackage releaseExpDir
    )

    // Read additional information from the release notes document
    let releaseNotesExpData =
        File.ReadAllLines "RELEASE_NOTES_EXPERIMENTAL.md"
        |> parseAllReleaseNotes

    let releaseExp = List.head releaseNotesExpData

    let msg =  releaseExp.Notes |> List.fold (fun r s -> r + s + "\n") ""
    let releaseExpMsg = (sprintf "Release %s\n" releaseExp.NugetVersion) + msg

    Target "ExpSetVersion" (fun _ ->
        printfn "Setting %s" releaseExpMsg
        setVersion releaseExp releaseExpDir
    )

    Target "ExpPublishToGallery" ( fun _ ->
        publishToGallery releaseExpDir
    )

    Target "ExpReleaseGitHub" (fun _ ->
        releaseGithub releaseExp
    )


// --------------------------------------------------------------------------------------
// Run generator by default. Invoke 'build <Target>' to override
// --------------------------------------------------------------------------------------

Target "Default" DoNothing
Target "Build" DoNothing
Target "Release" DoNothing
Target "ReleaseExp" DoNothing
Target "BuildPackages" DoNothing

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
==> "Build"

"YarnInstall" ==> "Build"
"DotNetRestore" ==> "Build"

"YarnInstall" ==> "ExpRunScript"
"DotNetRestore" ==> "ExpRunScript"

"Clean"
==> "ExpRunScript"
==> "ExpCopyAssets"
==> "ExpCopyDocs"
==> "ExpUpdatePackageId"
==> "ExpCopyFSAC"
==> "ExpCopyForge"
==> "ExpCopyGrammar"
==> "ExpCopySchemas"
==> "ExpSetVersion"
==> "BuildPackageExp"

"Build"
==> "SetVersion"
// ==> "InstallVSCE"
==> "BuildPackage"
==> "ReleaseGitHub"
==> "PublishToGallery"
==> "Release"

"BuildPackageExp"
==> "ExpReleaseGitHub"
==> "ExpPublishToGallery"
==> "ReleaseExp"

"BuildPackage" ==> "BuildPackages"
"BuildPackageExp" ==> "BuildPackages"

RunTargetOrDefault "Default"

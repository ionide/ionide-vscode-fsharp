namespace Ionide.VSCode.FSharp

//---------------------------------------------------
//Find path of F# install and FSI path
//---------------------------------------------------
// Below code adapted from similar in FSSutoComplete project
module Environment =
    open Fable.Core
    open Fable.Core.JsInterop
    open Fable.Import.vscode
    open Fable.Import.Node
    open Fable.Import.Node.fs_types
    open Ionide.VSCode.Helpers

    let isWin = ``process``.platform = "win32"

    let private (</>) a b =
        if isWin then a + @"\" + b
        else a + "/" + b

    let private dirExists dir =
        try
            fs.statSync(dir).isDirectory()
        with
        | _ -> false

    let private fileExists file =
        try
            fs.statSync(file).isFile()
        with
        | _ -> false

    let private getOrElse defaultValue option =
        match option with
        | None -> defaultValue
        | Some x -> x

    let private programFilesX86 =
        let wow64 = ``process``.env?``PROCESSOR_ARCHITEW6432`` |> unbox<string>
        let globalArch = ``process``.env?``PROCESSOR_ARCHITECTURE`` |> unbox<string>
        match wow64, globalArch with
        | "AMD64", "AMD64" | null, "AMD64" | "x86", "AMD64" -> ``process``.env?``ProgramFiles(x86)`` |> unbox<string>
        | _ -> ``process``.env?``ProgramFiles`` |> unbox<string>
        |> fun detected ->
            if detected = null then @"C:\Program Files (x86)\"
            else detected

    // Always returns host program files folder
    let private platformProgramFiles =
        programFilesX86
        |> String.replace " (x86)" ""


    let private getToolsPathWindows () =
        [ "4.1"; "4.0"; "3.1"; "3.0" ]
        |> List.map (fun v -> programFilesX86 </> @"\Microsoft SDKs\F#\" </> v </> @"\Framework\v4.0")
        |> List.tryFind dirExists

    let private getToolsPathFromConfiguration () =
        let cfg = workspace.getConfiguration ()
        let path = cfg.get("FSharp.toolsDirPath", "")
        if path <> "" && dirExists path then Some path
        else None

    let private getListDirectoriesToSearchForTools () =
        if isWin then
            [ getToolsPathFromConfiguration (); getToolsPathWindows () ]
        else
            [ getToolsPathFromConfiguration () ]
        |> List.choose id

    let private findFirstValidFilePath exeName directoryList =
        directoryList
        |> List.map (fun v -> v </> exeName)
        |> List.tryFind fileExists

    let private getFsiFilePath () =
        if isWin then
            let cfg = workspace.getConfiguration ()
            let fsiPath = cfg.get("FSharp.fsiFilePath", "")
            if fsiPath = ""  then "FsiAnyCpu.exe" else fsiPath
        else "fsharpi"

    let fsi =
        let fileName = getFsiFilePath ()
        let dirs = getListDirectoriesToSearchForTools ()
        match findFirstValidFilePath fileName dirs with
        | None -> fileName
        | Some x -> x

    // because the buffers from console output contain newlines, we need to trim them out if we want to have usable path inputs
    let spawnAndGetTrimmedOutput location linuxCmd command =
        Process.exec location linuxCmd command
        |> Promise.map (fun (err, stdoutBuf, stderrBuf) -> err, stdoutBuf |> string |> String.trim, stderrBuf |> string |> String.trim )

    let tryGetTool toolName =
        spawnAndGetTrimmedOutput "which" "" toolName
        |> Promise.map (fun (err, path, errs) -> if path <> "" then Some path else None )


    /// discover the path to msbuild by a) checking the user-specified configuration, and only if that's not present then try probing
    let msbuild =
        let configured = Configuration.get "" "FSharp.msbuildLocation"
        if configured <> ""
        then configured |> Promise.lift
        else
            if not isWin
            then
                let tools = [
                    "msbuild"
                    "xbuild"
                ]

                promise {
                    let! [msbuild; xbuild] = Promise.all (tools |> List.map tryGetTool) |> Promise.map Seq.toList
                    match msbuild, xbuild with
                    | Some m, _ -> return m
                    | _, Some x -> return x
                    | _, _ -> return "xbuild" // at this point nothing really matters because we don't have a sane default at all :(
                }
            else
                let MSBuildPath =
                    [ (programFilesX86 </> @"\MSBuild\14.0\Bin")
                      (programFilesX86 </> @"\MSBuild\12.0\Bin")
                      (programFilesX86 </> @"\MSBuild\12.0\Bin\amd64")
                      @"c:\Windows\Microsoft.NET\Framework\v4.0.30319\"
                      @"c:\Windows\Microsoft.NET\Framework\v4.0.30128\"
                      @"c:\Windows\Microsoft.NET\Framework\v3.5\" ]

                defaultArg (findFirstValidFilePath "MSBuild.exe" MSBuildPath) "msbuild.exe" |> Promise.lift

    let dotnet =
        let configured = Configuration.get "" "FSharp.dotnetLocation"
        if configured <> ""
        then configured |> Promise.lift
        else
            if not isWin
            then
                promise {
                    let! dotnet = tryGetTool "dotnet"
                    match dotnet with
                    | Some tool -> return tool
                    | None -> return "dotnet" // at this point nothing really matters because we don't have a sane default at all :(
                }
            else
                let dotnetPath =
                    [ (platformProgramFiles </> @"dotnet")
                      (programFilesX86 </> @"dotnet") ]

                defaultArg (findFirstValidFilePath "dotnet.exe" dotnetPath) "dotnet.exe" |> Promise.lift

namespace Ionide.VSCode.FSharp

//---------------------------------------------------
//Find path of F# install and FSI path
//---------------------------------------------------
// Below code adapted from similar in FSAutoComplete project
module Environment =

    open Fable.Core
    open Fable.Core.JsInterop
    open Fable.Import.Node
    open Ionide.VSCode.Helpers
    module node = Fable.Import.Node.Exports

    let isWin = Globals.``process``.platform = Base.NodeJS.Platform.Win32

    let private (</>) a b =
        if isWin then a + @"\" + b
        else a + "/" + b

    let private fileExists file =
        try
            node.fs.statSync(U2.Case1 file).isFile()
        with
        | _ -> false

    let private programFilesX86 =
        let wow64 = Globals.``process``.env?``PROCESSOR_ARCHITEW6432`` |> unbox<string>
        let globalArch = Globals.``process``.env?``PROCESSOR_ARCHITECTURE`` |> unbox<string>
        match wow64, globalArch with
        | "AMD64", "AMD64" | null, "AMD64" | "x86", "AMD64" -> Globals.``process``.env?``ProgramFiles(x86)`` |> unbox<string>
        | _ -> Globals.``process``.env?``ProgramFiles`` |> unbox<string>
        |> fun detected ->
            if detected = null then @"C:\Program Files (x86)\"
            else detected

    // Always returns host program files folder
    let private platformProgramFiles =
        programFilesX86
        |> String.replace " (x86)" ""

    let private findFirstValidFilePath exeName directoryList =
        directoryList
        |> List.map (fun v -> v </> exeName)
        |> List.tryFind fileExists

    let private fscFileName = if isWin then "Fsc.exe" else "fsharpc"

    let configFsiFilePath () =
        Configuration.tryGet "FSharp.fsiFilePath"

    let configFsiSdkFilePath () =
        Configuration.tryGet "FSharp.fsiSdkFilePath"

    let configFSCPath () =
        Configuration.tryGet "FSharp.fsiFilePath"
        |> Option.bind (fun path -> try Some (node.path.dirname path </> fscFileName) with _ -> None) //dirname could fail so wrap that

    // because the buffers from console output contain newlines, we need to trim them out if we want to have usable path inputs
    let spawnAndGetTrimmedOutput location linuxCmd command =
        Process.exec location linuxCmd command
        |> Promise.map (fun (err, stdoutBuf, stderrBuf) -> err, stdoutBuf |> string |> String.trim, stderrBuf |> string |> String.trim )

    let tryGetTool toolName =
        if isWin then
            spawnAndGetTrimmedOutput "cmd /C where" "" toolName
            |> Promise.map (fun (err, path, errs) -> if path <> "" then Some path else None )
            |> Promise.map (Option.bind (fun paths -> paths.Split('\n') |> Array.map (String.trim) |> Array.tryHead))
        else
            spawnAndGetTrimmedOutput "which" "" toolName
            |> Promise.map (fun (err, path, errs) -> if path <> "" then Some path else None )

    let configMSBuildPath = Configuration.tryGet "FSharp.msbuildLocation"

    let dotnet =
        Configuration.tryGet "FSharp.dotnetRoot"
        |> Option.map (fun root ->
            root </> (if isWin then "dotnet.exe" else "dotnet")
            |> Some
            |> Promise.lift)
        |> Option.defaultWith (fun () -> tryGetTool "dotnet" )

    let mono =
        Configuration.tryGet "FSharp.monoPath"
        |> Option.map ( Some >> Promise.lift)
        |> Option.defaultWith (fun () -> tryGetTool "mono")

    let ensureDirectory (path : string) =
        let root =
            if node.path.isAbsolute path then
                None
            else
                Some Globals.__dirname

        let segments =
            path.Split [| char node.path.sep |]
            |> Array.toList

        let rec ensure segments currentPath =
            match segments with
            | head::tail ->
                if head = "" then
                    ensure tail currentPath
                else
                    let subPath =
                        match currentPath with
                        | Some path -> node.path.join(path, head)
                        | None -> head
                    if not (node.fs.existsSync !^subPath) then
                        node.fs.mkdirSync subPath
                    ensure tail (Some subPath)
            | [] -> ()

        ensure segments root
        path

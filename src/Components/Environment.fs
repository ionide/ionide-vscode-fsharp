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

    let private isWin = ``process``.platform = "win32"

    let private (</>) a b =
        if isWin then a + @"\" + b
        else a + "/" + b

    let private dirExists dir = fs.statSync(dir).isDirectory()

    let private fileExists file = fs.statSync(file).isFile()

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

    let private getToolsPathWindows () =
        [ "4.0"; "3.1"; "3.0" ]
        |> List.map (fun v -> programFilesX86 </> @"\Microsoft SDKs\F#\" </> v </> @"\Framework\v4.0")
        |> List.tryFind dirExists

    let private getToolsPathFromConfiguration () =
        let cfg = workspace.getConfiguration ()
        let path = cfg.get("FSharp.toolsDirPath", "")
        if not (path = "") && dirExists path then Some path
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

    let fsi =
        let fileName = if isWin then "fsi.exe" else "fsharpi"
        let dirs = getListDirectoriesToSearchForTools ()
        match findFirstValidFilePath fileName dirs with
        | None -> fileName
        | Some x -> x

namespace Ionide.VSCode.FSharp

//---------------------------------------------------
//Find path of F# install and FSI path
//---------------------------------------------------
// Below code adapted from similar in FSSutoComplete project
module Environment =
    open Fable.Core
    open Fable.Import.Node
    open Fable.Import.Node.fs_types

    let isWin = ``process``.platform = "win32"

    let (</>) a b =
        if isWin then a + @"\" + b
        else a + "/" + b

    let dirExists dir = fs.statSync(dir).isDirectory()

    let getOrElse defaultValue option =
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

    let private fsharpInstallationPath () =
        [ "4.0"; "3.1"; "3.0" ]
        |> List.map (fun v -> programFilesX86 </> @"\Microsoft SDKs\F#\" </> v </> @"\Framework\v4.0")
        |> List.tryFind dirExists

    let fsi =
        if not isWin then "fsharpi"
        else getOrElse "" (fsharpInstallationPath ()) </> "fsi.exe"

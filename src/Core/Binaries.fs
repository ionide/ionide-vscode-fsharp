namespace Ionide.VSCode.FSharp

open Ionide.VSCode.Helpers

/// This module encapsulates finding fsharpc/fsharpi/masbuild binaries for the current session.
/// It defers to local configuration (via VSCode settings files), but if those are not present uses 
/// FSAC to provide the locations.
module Binaries = 

    let private fsacConfig () = 
        LanguageService.compilerLocation () 
        |> Promise.map (fun c -> c.Data)

    let fsi () = 
        promise {
            match Environment.configFSIPath with
            | Some path -> return Some path
            | None ->
                let! fsacPaths = fsacConfig ()
                return fsacPaths.Fsi
        }

    let fsc () = 
        promise {
            match Environment.configFSCPath with
            | Some path -> return Some path
            | None ->
                let! fsacPaths = fsacConfig ()
                return fsacPaths.Fsc
        }

    let msbuild () =
        promise {
            match Environment.configMSBuildPath with
            | Some path -> return Some path
            | None ->
                let! fsacPaths = fsacConfig ()
                return fsacPaths.MSBuild
        } 
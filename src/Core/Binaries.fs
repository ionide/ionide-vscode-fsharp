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
        Environment.configFSIPath |> Option.map Promise.lift
        |> Option.defaultWith (fsacConfig >> Promise.map (fun paths -> paths.Fsi))

    let fsc () = 
        Environment.configFSCPath |> Option.map Promise.lift
        |> Option.defaultWith (fsacConfig >> Promise.map (fun paths -> paths.Fsc))
        
    let msbuild () = 
        Environment.configMSBuildPath |> Option.map Promise.lift
        |> Option.defaultWith (fsacConfig >> Promise.map (fun paths -> paths.MSBuild))

namespace Ionide.VSCode.FSharp

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.Node
open Fable.Import.vscode
open Ionide.VSCode.Helpers

[<RequireQualifiedAccess>]
module VsCodeIconTheme =
    let logger = ConsoleAndOutputChannelLogger(Some "icons", Level.DEBUG, None, Some Level.DEBUG)

    type Info = {
        id: string
        label: string
        path: string
        ext: Extension<obj>
    }

    let getAllInfo () = seq {
        for ext in extensions.all do
            let packageJson = ext.packageJSON
            let contributes = unbox packageJson?contributes
            if JS.isDefined contributes then
                let iconThemes: seq<obj> = unbox contributes?iconThemes
                if JS.isDefined iconThemes then
                    for theme in iconThemes do
                        let id: string = unbox theme?id
                        let path: string = unbox theme?path
                        let label: string = unbox theme?label
                        yield { id = id; path = path; ext = ext; label = label }
    }

    let getInfo (themeId: string) =
        getAllInfo () |> Seq.tryFind (fun t -> t.id = themeId)

    let getConfigured () =
        let workbenchConfig = workspace.getConfiguration("workbench")
        let themeId = workbenchConfig.get<string>("iconTheme")

        Option.ofObj themeId

    let inline private fallback fallbackFunc opt = match opt with |Some x -> Some x |None -> fallbackFunc ()
    let inline private fallbackValue fallback opt = match opt with |Some x -> Some x |None -> fallback

    type IconDefinition =
        abstract iconPath: string option with get, set

    type SpecificIconTheme =
        abstract member file: string with get, set
        abstract member folder: string with get, set
        abstract member folderExpanded: string with get, set
        abstract member folderNames: JsObjectAsDictionary<string> with get, set
        abstract member folderNamesExpanded: JsObjectAsDictionary<string> with get, set
        abstract member languageIds: JsObjectAsDictionary<string> with get, set
        abstract member fileExtensions: JsObjectAsDictionary<string> with get, set
        abstract member fileNames: JsObjectAsDictionary<string> with get, set

    type IconTheme =
        inherit SpecificIconTheme

        abstract member iconDefinitions: JsObjectAsDictionary<IconDefinition> with get, set
        abstract member light: SpecificIconTheme with get, set
        abstract member highContrast: SpecificIconTheme with get, set

    let private getPossibleFileExtensions (fileName: string) =
        let result = ResizeArray<string>()

        let mutable dotPos = fileName.IndexOf(".")
        while dotPos <> -1 do
            let ext = fileName.Substring(dotPos + 1)
            result.Add(ext)
            dotPos <-
                if dotPos + 1 < fileName.Length then
                    fileName.IndexOf(".", dotPos + 1)
                else
                    -1

        result

    let inline private findByFileName (name: string) (theme: SpecificIconTheme) =
        if JS.isDefined theme.fileNames then theme.fileNames.tryGet name else None

    let inline private findByFolderName (name: string) (theme: SpecificIconTheme) =
        if JS.isDefined theme.folderNames then theme.folderNames.tryGet name else None

    let inline private findByExpandedFolderName (name: string) (theme: SpecificIconTheme) =
        if JS.isDefined theme.folderNamesExpanded then theme.folderNamesExpanded.tryGet name else None

    let inline private findByLanguageId (languageId: string Option) (theme: SpecificIconTheme) =
        if JS.isDefined theme.languageIds && languageId.IsSome then
            theme.languageIds.tryGet (languageId.Value)
        else
            None

    let inline private findByExtension (name: string) (theme: SpecificIconTheme) =
        if JS.isDefined theme.fileExtensions then
            let exts = getPossibleFileExtensions name
            let matching = exts |> Seq.tryFind theme.fileExtensions.hasOwnProperty
            match matching with
            | Some matching -> theme.fileExtensions.tryGet matching
            | None -> None
        else
            None

    let inline private fileDefault (theme: SpecificIconTheme) =
        if JS.isDefined theme.file then Some theme.file else None

    let inline private folderDefault (theme: SpecificIconTheme) =
        if JS.isDefined theme.folder then Some theme.folder else None

    let inline private folderExpandedDefault (theme: SpecificIconTheme) =
        if JS.isDefined theme.folderExpanded then Some theme.folderExpanded else None

    let private getFileIconKey (name: string) (languageId: string Option) (useDefault: bool) (theme: SpecificIconTheme) =
        if JS.isDefined theme then
            findByFileName name theme
            |> fallback (fun () -> findByLanguageId languageId theme)
            |> fallback (fun () -> findByExtension name theme)
            |> fallback (fun () -> if useDefault then fileDefault theme else None)
        else
            None

    let private getFolderIconKey (name: string) (theme: SpecificIconTheme) =
        if JS.isDefined theme then
            findByFolderName name theme
            |> fallback (fun () -> folderDefault theme)
        else
            None

    let private getExpandedFolderIconKey (name: string) (theme: SpecificIconTheme) =
        if JS.isDefined theme then
            findByExpandedFolderName name theme
            |> fallback (fun () -> folderExpandedDefault theme)
            |> fallback (fun () -> getFolderIconKey name theme)
        else
            None

    let private getIconPath (id: string option) (theme: IconTheme) =
        id |> Option.bind theme.iconDefinitions.tryGet |> Option.bind (fun x -> x.iconPath)

    let private readFile path : JS.Promise<Buffer.Buffer> =
        Promise.create(fun resolve reject ->
            Fs.readFile(path, fun err buffer ->
                if JS.isDefined err then
                    reject err
                else
                    resolve buffer
            )
        )

    type Loaded = {
        info: Info
        theme: IconTheme
    }

    let private resolveLoadedPath (loaded: Loaded) path =
        let themeFileFullPath = Path.join(loaded.info.ext.extensionPath, loaded.info.path)
        let themeFileDir = Path.dirname(themeFileFullPath)
        Path.join(themeFileDir, path)

    let private parseMsJson<'a> (str: string) =
        let clean = Regex("//.*$", RegexOptions.Multiline).Replace(str, "")
        try
            Some (unbox (JS.JSON.parse(clean)))
        with
        | ex ->
            logger.Error("Failed to parse JSON: %s", str)
            None

    let load (info: Info) =
        let fullPath = Path.join(info.ext.extensionPath, info.path)
        logger.Debug("Loading icon theme from %s", fullPath)
        promise {
            let! fileBuffer = readFile fullPath
            let parsed = parseMsJson<IconTheme> (fileBuffer.toString())

            return parsed |> Option.map(fun theme -> { info = info; theme = theme })
        }

    type ResolvedIcon = {
        dark: string option
        light: string option
        highContrast: string option
    }

    let private resolve (f: SpecificIconTheme -> string option) (loaded: Loaded) =
        let darkId = f loaded.theme
        let lightId = f loaded.theme.light |> fallbackValue darkId
        let highContrastId = f loaded.theme.highContrast |> fallbackValue darkId

        {
            dark = loaded.theme |> getIconPath darkId |> Option.map (resolveLoadedPath loaded)
            light = loaded.theme |> getIconPath lightId |> Option.map (resolveLoadedPath loaded)
            highContrast = loaded.theme |> getIconPath highContrastId |> Option.map (resolveLoadedPath loaded)
        }

    let getFileIcon (name: string) (languageId: string option) (useDefault: bool) (loaded: Loaded) =
        let nameLower = name.ToLowerInvariant()
        resolve (getFileIconKey nameLower languageId useDefault) loaded

    let getFolderIcon (name: string) (loaded: Loaded) =
        let nameLower = name.ToLowerInvariant()
        resolve (getFolderIconKey nameLower) loaded

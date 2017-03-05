namespace Ionide.VSCode.FSharp

open System
open Fable.Core
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Fable.Core.JsInterop
open DTO
open Ionide.VSCode.Helpers

module CodeLens =
    let mutable cache: Map<string, CodeLens[]> = Map.empty
    let refresh = EventEmitter<int>()
    let mutable private version = 0
    let mutable private flag = true

    let mutable changes : TextDocumentContentChangeEvent list = []

    let private heightChange (change : TextDocumentContentChangeEvent) =
        let oldHeight = change.range.``end``.line - change.range.start.line  |> int
        let newHeight = change.text.ToCharArray() |> Seq.sumBy (fun n -> if n = '\n' then 1 else 0)
        newHeight - oldHeight

    let private createProvider () =
        let symbolsToCodeLens (doc : TextDocument) (symbols: Symbols[]) : CodeLens[] =
             symbols |> Array.collect (fun syms ->
                let range = CodeRange.fromDTO syms.Declaration.BodyRange
                let codeLens = CodeLens range

                let codeLenses =  syms.Nested |> Array.choose (fun sym ->
                    if sym.GlyphChar <> "Fc"
                       && sym.GlyphChar <> "M"
                       && sym.GlyphChar <> "F"
                       && sym.GlyphChar <> "P"
                       || sym.IsAbstract
                       || sym.EnclosingEntity = "I"  // interface
                       || sym.EnclosingEntity = "R"  // record
                       || sym.EnclosingEntity = "D"  // DU
                       || sym.EnclosingEntity = "En" // enum
                       || sym.EnclosingEntity = "E"  // exception
                    then None
                    else Some (CodeLens (CodeRange.fromDTO sym.BodyRange)))

                if syms.Declaration.GlyphChar <> "Fc" then codeLenses
                else
                    codeLenses |> Array.append [|codeLens|])

        let formatSignature (sign : string) : string =
            let sign =
                match sign with
                | StartsWith "val" _
                | StartsWith "member" _
                | StartsWith "abstract" _
                | StartsWith "static" _
                | StartsWith "override" _ -> sign
                | _ ->
                    match sign.IndexOf "(" with
                    | i when i > 0 ->
                        sign.Substring(0, i) + ":" + sign.Substring(i+1)
                    | _ -> sign

            let sign = if sign.Contains ":" then sign.Split(':').[1 ..] |> String.concat ":" else sign
            let parms = sign.Split([|"->"|], StringSplitOptions.RemoveEmptyEntries)
            parms
            |> Seq.map (function
                | Contains "(requires" p -> p
                | Contains "*" p ->
                    p.Split '*' |> Seq.map (fun z -> if z.Contains ":" then z.Split(':').[1] else z) |> String.concat "* "
                | Contains ":" p ->
                    p.Split(':').[1]
                | p -> p)
            |> Seq.map String.trim
            |> String.concat " -> "

        { new CodeLensProvider with
            member __.provideCodeLenses(doc, _) =
                // printf "[%d:%d:%d:%d] CodeLens - provide called" DateTime.Now.Hour DateTime.Now.Minute DateTime.Now.Second  DateTime.Now.Millisecond
                promise {
                    let! data =
                        if flag then
                            promise {
                                let! result = LanguageService.declarations doc.fileName version
                                // printf "CodeLens - FileName: %s, Version: %d" doc.fileName version
                                return
                                    if isNotNull result then
                                        let res = symbolsToCodeLens doc result.Data
                                        // printf "[%d:%d:%d:%d] CodeLens - Result from FSAC. %A" DateTime.Now.Hour DateTime.Now.Minute DateTime.Now.Second  DateTime.Now.Millisecond res
                                        res
                                    else [||]
                            }
                        else
                            Promise.lift [||]
                    let d =
                        if data.Length > 0 && flag then
                            cache <- cache |> Map.add doc.fileName data
                            changes <- []
                            data
                        else
                            let chngs = changes |>  List.choose (fun n -> let r = heightChange n in if r = 0 then None else Some (n.range.start.line, r) )
                            let fromCache = defaultArg (cache |> Map.tryFind doc.fileName) [||]
                            // printf "CodeLens - FileName: %s, Version: %d" doc.fileName version
                            let res =
                                fromCache
                                |> Array.map (fun n ->
                                    let hChange = chngs |> Seq.sumBy(fun (ln,h) -> if ln <= n.range.start.line then h else 0)
                                    let ln = n.range.start.line + unbox hChange
                                    let range = vscode.Range(ln, n.range.start.character, ln, n.range.``end``.character)

                                    n.range <- range
                                    n
                                )
                            // printf "[%d:%d:%d:%d] CodeLens - Result from cache. %A" DateTime.Now.Hour DateTime.Now.Minute DateTime.Now.Second  DateTime.Now.Millisecond res
                            // printf "CodeLens - Chngs: %A" chngs
                            cache <- cache |> Map.add doc.fileName res
                            changes <- []
                            res
                    flag <- false
                    return ResizeArray d
                }
                // |> Promise.catch (fun _ -> Promise.lift <| ResizeArray())
                |> Case2

            member __.resolveCodeLens(codeLens, _) =
                promise {
                    let! signaturesResult =
                        LanguageService.signature
                            window.activeTextEditor.document.fileName
                            (int codeLens.range.start.line + 1)
                            (int codeLens.range.start.character + 1)
                    let cmd = createEmpty<Command>
                    cmd.title <- if isNotNull signaturesResult then formatSignature signaturesResult.Data else ""
                    codeLens.command <- cmd
                    return codeLens

                } |> Case2

            member __.onDidChangeCodeLenses = unbox refresh.event
        }

    let private textChangedHandler (event : TextDocumentChangeEvent) =
        if isNotNull window.activeTextEditor && event.document.fileName = window.activeTextEditor.document.fileName  then
            // printf "[%d:%d:%d:%d] CodeLens - File changed." DateTime.Now.Hour DateTime.Now.Minute DateTime.Now.Second  DateTime.Now.Millisecond
            changes <- [yield! changes; yield! event.contentChanges]
            refresh.fire(-1)
        ()

    let private fileOpenedHandler (event : TextEditor) =
        if isNotNull event then
            changes <- []
            version <- unbox event.document.version
            flag <- true
        ()

    let activate selector (disposables: Disposable[]) =
        refresh.event.Invoke(fun (n) -> version <- n; flag <- n > 0; null) |> ignore
        workspace.onDidChangeTextDocument $ (textChangedHandler,(), disposables) |> ignore
        window.onDidChangeActiveTextEditor $ (fileOpenedHandler, (), disposables) |> ignore



        languages.registerCodeLensProvider(selector, createProvider()) |> ignore
        refresh.fire (1)
        ()

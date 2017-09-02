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
    let mutable private fileName = ""

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

        let formatSignature (sign : SignatureData) : string =
            let args =
                sign.Parameters
                |> List.map (fun group ->
                    group |> List.map (fun p ->
                        p.Type
                    )
                    |> String.concat " * "
                )
                |> String.concat " -> "

            if String.IsNullOrEmpty args then sign.OutputType else args + " -> " + sign.OutputType

        { new CodeLensProvider with
            member __.provideCodeLenses(doc, _) =
                // printf "[%d:%d:%d:%d] CodeLens - provide called" DateTime.Now.Hour DateTime.Now.Minute DateTime.Now.Second  DateTime.Now.Millisecond
                promise {
                    // printf "[%d:%d:%d:%d] CodeLens - Active document name - %s, version: %d; Asked document name - %s, version: %d" DateTime.Now.Hour DateTime.Now.Minute DateTime.Now.Second  DateTime.Now.Millisecond (window.activeTextEditor.document.fileName) (unbox window.activeTextEditor.document.version) doc.fileName version

                    let! data =
                        if (unbox window.activeTextEditor.document.version) = version then
                            // printf "CodeLens - From FSAC"
                            promise {
                                let! result = LanguageService.declarations doc.fileName version
                                // printf "[%d:%d:%d:%d] CodeLens - FileName: %s, Version: %d" DateTime.Now.Hour DateTime.Now.Minute DateTime.Now.Second  DateTime.Now.Millisecond doc.fileName version
                                return
                                    if isNotNull result then
                                        let res = symbolsToCodeLens doc result.Data
                                        // printf "[%d:%d:%d:%d] CodeLens - Result from FSAC. %A" DateTime.Now.Hour DateTime.Now.Minute DateTime.Now.Second  DateTime.Now.Millisecond res
                                        res
                                    else [||]
                            }
                        else
                            // printf "CodeLens - Old request"
                            promise.Return [||]


                    let d =
                        if data.Length > 0 then
                            cache <- cache |> Map.add doc.fileName data
                            changes <- []
                            data
                        else
                            let chngs = changes |>  List.choose (fun n -> let r = heightChange n in if r = 0 then None else Some (n.range.start.line, r) )
                            let fromCache = defaultArg (cache |> Map.tryFind doc.fileName) [||]
                            let res =
                                fromCache
                                |> Array.map (fun n ->
                                    let hChange = chngs |> Seq.sumBy(fun (ln,h) -> if (ln - 1.) <= n.range.start.line then h else 0)
                                    let ln = n.range.start.line + unbox hChange
                                    let range = vscode.Range(ln, n.range.start.character, ln, n.range.``end``.character)

                                    // printfn "CodeLens - Old ln - %d, New ln - %d" ( unbox n.range.start.line) (unbox ln)
                                    n.range <- range
                                    n
                                )
                            // printf "[%d:%d:%d:%d] CodeLens - Result from cache. %A" DateTime.Now.Hour DateTime.Now.Minute DateTime.Now.Second  DateTime.Now.Millisecond res
                            // printf "CodeLens - Chngs: %A" chngs
                            cache <- cache |> Map.add doc.fileName res
                            changes <- changes |> List.skip (chngs |> List.length)
                            res
                    return ResizeArray d
                }
                // |> Promise.catch (fun _ -> Promise.lift <| ResizeArray())
                |> U2.Case2

            member __.resolveCodeLens(codeLens, _) =
                promise {
                    let! signaturesResult =
                        LanguageService.signatureData
                            window.activeTextEditor.document.fileName
                            (int codeLens.range.start.line + 1)
                            (int codeLens.range.start.character + 1)
                    let cmd = createEmpty<Command>
                    cmd.title <- if isNotNull signaturesResult then formatSignature signaturesResult.Data else ""
                    codeLens.command <- cmd
                    return codeLens

                } |> U2.Case2

            member __.onDidChangeCodeLenses = unbox refresh.event
        }

    let private textChangedHandler (event : TextDocumentChangeEvent) =
        if isNotNull window.activeTextEditor && event.document.fileName = fileName  then
            // printf "[%d:%d:%d:%d] CodeLens - File changed." DateTime.Now.Hour DateTime.Now.Minute DateTime.Now.Second  DateTime.Now.Millisecond
            changes <- [yield! changes; yield! event.contentChanges]
            refresh.fire(-1)
        ()



    let private fileOpenedHandler (event : TextEditor) =
        if isNotNull event then
            match event.document with
            | Document.FSharp ->
                changes <- []
                version <- unbox event.document.version
                fileName <- event.document.fileName
            | _ -> ()

        ()

    let activate selector (disposables: Disposable[]) =
        refresh.event.Invoke(fun (n) -> version <- n; null) |> ignore
        workspace.onDidChangeTextDocument $ (textChangedHandler,(), disposables) |> ignore
        window.onDidChangeActiveTextEditor $ (fileOpenedHandler, (), disposables) |> ignore

        fileOpenedHandler window.activeTextEditor


        languages.registerCodeLensProvider(selector, createProvider()) |> ignore
        refresh.fire (1)
        ()

namespace Ionide.VSCode.FSharp

open System
open System.Text
open System.Text.RegularExpressions
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Fable.Import.vscode
open Fable.Import.Node
open Ionide.VSCode.Helpers
open DTO

module Definition =
    module FromFindDeclaration =
        let private mapFindDeclarationResult (doc : TextDocument) (pos : Position) (o : FindDeclarationResult) : Definition option =
            if isNotNull o then
                let loc = createEmpty<Location>
                let range = doc.getWordRangeAtPosition pos
                let length = range.``end``.character - range.start.character
                loc.uri <- Uri.file o.Data.File
                loc.range <- CodeRange.fromDeclaration o.Data length
                loc |> Case1 |> Some
            else
                None

        let provide (doc : TextDocument) (pos : Position) = promise {
            let! res = LanguageService.findDeclaration (doc.fileName) (int pos.line + 1) (int pos.character + 1)
            return mapFindDeclarationResult doc pos res
        }

    module FromLoad =
        let private unescapeStandardString (s: string) =
            let mutable result = ""
            let mutable escaped = false
            let mutable unicodeHeaderChar = '?'
            let mutable remainingUnicodeChars = 0
            let mutable currentUnicodeChars = ""

            for i in [0 .. s.Length - 1] do
                let c = s.[i]
                if remainingUnicodeChars > 0 then
                    if (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') then
                        currentUnicodeChars <- currentUnicodeChars + string(c)
                        remainingUnicodeChars <- remainingUnicodeChars - 1

                        if remainingUnicodeChars = 0 then
                            result <- result + string(char(Convert.ToUInt32(currentUnicodeChars, 16)))
                    else
                        // Invalid unicode sequence, bail out
                        result <- result + "\\" + string(unicodeHeaderChar) + currentUnicodeChars + string(c)
                        remainingUnicodeChars <- 0
                else if escaped then
                    escaped <- false
                    match c with
                    | 'b' -> result <- result + "\b"
                    | 'n' -> result <- result + "\n"
                    | 'r' -> result <- result + "\r"
                    | 't' -> result <- result + "\t"
                    | '\\' -> result <- result + "\\"
                    | '"' -> result <- result + "\""
                    | ''' -> result <- result + "'"
                    | 'u' ->
                        unicodeHeaderChar <- 'u'
                        currentUnicodeChars <- ""
                        remainingUnicodeChars <- 4
                    | 'U' ->
                        unicodeHeaderChar <- 'U'
                        currentUnicodeChars <- ""
                        remainingUnicodeChars <- 8
                    | _ -> result <- result + "\\" + string(c)
                else if c = '\\' then
                    escaped <- true
                else
                    result <- result + string(c)

            result

        let private unescapeString (s: string) =
            if s.StartsWith("@\"") && s.Length >= 3 then
                s.Substring(2, s.Length - 3).Replace("\"\"", "\"")
            else if s.StartsWith("\"\"\"") && s.EndsWith("\"\"\"") && s.Length >= 6 then
                s.Substring(3, s.Length - 6)
            else if s.StartsWith("\"") && s.Length >= 2 then
                unescapeStandardString (s.Substring(1, s.Length - 2))
            else
                ""

        let private loadRegex = Regex(@"#load\s+(.+"")")

        let private tryParseLoad (line: string) =
            let m = loadRegex.Match(line)
            if m.Success then
                Some (unescapeString (m.Groups.[1].Value))
            else
                None

        let provide (doc : TextDocument) (pos : Position) : Definition option =
            let line = doc.lineAt(pos.line)
            match tryParseLoad line.text with
            | None -> None
            | Some filePath ->
                let dir = path.dirname(doc.fileName)
                let absolute = path.resolve(dir, filePath)
                Location(Uri.file absolute, Case2 (Position(0., 0.))) |> Case1 |> Some

    let private createProvider () =
        { new DefinitionProvider
          with
            member this.provideDefinition(doc, pos, ct) =
                promise {
                    let! fromFindDeclaration = FromFindDeclaration.provide doc pos
                    return
                        match fromFindDeclaration with
                        | Some def -> def
                        | None ->
                            match FromLoad.provide doc pos with
                            | Some def -> def
                            | None -> unbox None
                } |> Case2
        }

    let activate selector (disposables: Disposable[]) =
        languages.registerDefinitionProvider(selector, createProvider())
        |> ignore

        ()

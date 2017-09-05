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
                loc |> U2.Case1 |> Some
            else
                None

        let provide (doc : TextDocument) (pos : Position) = promise {
            let! res = LanguageService.findDeclaration (doc.fileName) (int pos.line + 1) (int pos.character + 1)
            return mapFindDeclarationResult doc pos res
        }

    module FromLoad =
        /// Remove escaping from standard (non verbatim & non triple quotes) string
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

            if remainingUnicodeChars > 0 then
                result <- result + "\\" + string(unicodeHeaderChar) + currentUnicodeChars
            else if escaped then
                result <- result + "\\"

            result

        let private loadRegex = Regex(@"#load\s+")
        let private standardStringRegex = Regex(@"^""(((\\"")|[^""])*)""")
        let private verbatimStringRegex = Regex(@"^@""((""""|[^""])*)""")
        let private tripleStringRegex = Regex(@"^""""""(.*?)""""""")

        /// Get the string starting at index in any of the string forms (standard, verbatim or triple quotes)
        let private tryParseStringFromStart (s: string) (index: int) =
            let s = s.Substring(index)
            let verbatim = verbatimStringRegex.Match(s)
            if verbatim.Success then
                let s = verbatim.Groups.[1].Value
                Some (s.Replace("\"\"", "\""))
            else
                let triple = tripleStringRegex.Match(s)
                if triple.Success then
                    let s = triple.Groups.[1].Value
                    Some s
                else
                    let standard = standardStringRegex.Match(s)
                    if standard.Success then
                        let s = standard.Groups.[1].Value
                        Some (unescapeStandardString s)
                    else
                        None

        /// Parse the content of a "#load" instruction that start somewhere before `startPoint`
        let private tryParseLoad (line: string) (startPoint: int) =
            let potential = seq {
                let matches = loadRegex.Matches(line)
                for i in [0 .. matches.Count - 1] do
                    let m = matches.[i]
                    if m.Index <= startPoint then
                        yield m
            }

            match potential |> Seq.tryLast with
            | Some m ->
                let stringIndex = m.Index + m.Length
                tryParseStringFromStart line stringIndex
            | None -> None

        let provide (doc : TextDocument) (pos : Position) : Definition option =
            let line = doc.lineAt(pos.line)
            match tryParseLoad line.text (int(pos.character)) with
            | None -> None
            | Some filePath ->
                let dir = Path.dirname(doc.fileName)
                let absolute = Path.resolve(dir, filePath)
                Location(Uri.file absolute, U2.Case2 (Position(0., 0.))) |> U2.Case1 |> Some

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
                } |> U2.Case2
        }

    let activate selector (context: ExtensionContext) =
        languages.registerDefinitionProvider(selector, createProvider())
        |> context.subscriptions.Add

        ()

namespace Ionide.VSCode.FSharp.Import

open Fable.Core
open Fable.Core.JsInterop

module XmlDoc =
    type XmlDoc =
        class
        end

    let private dom: obj = import "DOMParser" "xmldom"

    let mkDoc (xmlContent: string) : XmlDoc =
        emitJsExpr (dom, xmlContent) "new $0().parseFromString($1)"

module XPath =
    /// return value will be a Node, Attr, string, int or bool
    /// See https://github.com/goto100/xpath/blob/master/xpath.d.ts
    type SelectXPath = System.Func<string, XmlDoc.XmlDoc, obj>

    type XPath =
        abstract member useNamespaces: obj -> SelectXPath

    [<ImportAll("xpath")>]
    let xpath: XPath = jsNative

    let private selectWith (select: SelectXPath) (xmlDoc: XmlDoc.XmlDoc) (xpath: string) = select.Invoke(xpath, xmlDoc)

    type XPathSelector(xmlDoc, ns) =
        let selectXPath = xpath.useNamespaces {| t = ns |}

        member this.Select<'t>(xpath: string) : 't = !! selectWith selectXPath xmlDoc xpath

        member this.SelectString(xpath: string) : string = this.Select<string>($"string({xpath})")

        member this.TrySelectString(xpath: string) : string option =
            let s = this.SelectString(xpath)

            if System.String.IsNullOrWhiteSpace(s) then
                None
            else
                Some s

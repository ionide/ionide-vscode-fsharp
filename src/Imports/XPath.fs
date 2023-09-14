namespace Ionide.VSCode.FSharp.Import

open Fable.Core
open Fable.Core.JsInterop

module XmlDoc =
    type XmlNode =
        class
        end

    type XmlDoc =
        class
            inherit XmlNode
        end

    let private dom: obj = import "DOMParser" "xmldom"

    let mkDoc (xmlContent: string) : XmlDoc =
        emitJsExpr (dom, xmlContent) "new $0().parseFromString($1)"

module XPath =
    /// return value will be a Node, Attr, string, int or bool
    /// See https://github.com/goto100/xpath/blob/master/xpath.d.ts
    type SelectXPath = System.Func<string, XmlDoc.XmlNode, bool, obj>

    type XPath =
        abstract member useNamespaces: obj -> SelectXPath

    [<ImportAll("xpath")>]
    let xpath: XPath = jsNative

    type SelectCardinality =
        | Single
        | Many

    module SelectCardinality =
        let toBool =
            function
            | Single -> true
            | Many -> false

    let private selectWith
        (select: SelectXPath)
        (xmlNode: XmlDoc.XmlNode)
        (xpath: string)
        (selectSingle: SelectCardinality)
        =
        select.Invoke(xpath, xmlNode, SelectCardinality.toBool selectSingle)

    type XPathSelector(xmlDoc, ns) =
        let selectXPath = xpath.useNamespaces {| t = ns |}

        member this.SelectNodes(xpath: string) : XmlDoc.XmlNode array =
            !! selectWith selectXPath xmlDoc xpath Many

        member this.SelectStringRelative(node: XmlDoc.XmlNode, xpath: string) : string =
            !! selectWith selectXPath node $"string({xpath})" Single

        member this.SelectString(xpath: string) : string =
            this.SelectStringRelative(xmlDoc, xpath)

        member this.SelectStrings(xpath: string) : string array = !! this.SelectNodes($"string({xpath})")

        member this.TrySelectStringRelative(node: XmlDoc.XmlNode, xpath: string) : string option =
            let s = this.SelectStringRelative(node, xpath)

            if System.String.IsNullOrWhiteSpace(s) then None else Some s

        member this.TrySelectString(xpath: string) : string option =
            this.TrySelectStringRelative(xmlDoc, xpath)

namespace Ionide.VSCode.FSharp

open Fable.Core
open Fable.Core.JsInterop

module Webviews =
    open Fable.Import.VSCode.Vscode
    // These functions are recommended to used in conjunction with
    // Highlight HTML/SQL templates in F#
    // https://marketplace.visualstudio.com/items?itemName=alfonsogarciacaro.vscode-template-fsharp-highlight
    let inline css (content: string) = content
    let inline html (content: string) = content
    let inline js (content: string) = content

    // string concatenation is super fast for JS and we're very likely merging a lot of strings
    // on bigger files, so we'll omit the string builder here
    [<Emit("$0 += $1;")>]
    let private addString existing appending : unit = jsNative

    let private mergeStrings (rows: string seq) =
        let mutable str = ""

        for row in rows do
            addString str $"{row}\n"

        html $"<style>{str}</style>"

    let private mergeScripts (rows: string seq) =
        let mutable str = $""

        for row in rows do
            if not (System.String.IsNullOrWhiteSpace row) then
                // we'll surround each script with a iife to variable scope issues
                // and to ensure we're not polluting other scripts
                let row = js $"(() => {{ {row} }})();"

                addString str $"{row}\n"
        // we'll surround the scripts with the DOMContentLoaded event
        // to ensure all DOM elements already exist when executing these scripts
        html
            $"""<script>
            window.addEventListener('DOMContentLoaded', (event) => {{ {str} }});
            </script>"""

    /// <summary>
    /// Helper class to render HTML elements from the VSCode UI Toolkit
    /// </summary>
    type VsHtml =

        /// <summary>
        /// renders a datagrid with the provided data, the data must come in the shape of
        /// an array of anonymous records to allow Fable to simply deserialize the data to plain JS objects and arrays
        /// </summary>
        /// <remarks>
        /// The data must be an array of anonymous records, if no custom titles are provided the header will be the name of the property
        /// </remarks>
        /// <remarks>
        /// If you want to ensure the order of the headers or custom headers you can provide a ("My Header", "proeprtyName") tuple array.
        /// The second value in the tuple must match the property name of the record.
        /// </remarks>
        /// <param name="id">the DOM id string that will be used for the datagrid element</param>
        /// <param name="values">An anonymous record array with contains the data to render</param>
        /// <param name="customTitles">An array of string tuples to ensure header titles and order of colums</param>
        /// <example>
        /// <code lang="F#">
        /// let values =
        ///   [| {| name = "Peter"; age = 10 |}
        ///      {| name = "Frank"; age = 2 |} |]
        /// let headers =
        ///  [| ("User Name", "name")
        ///    ("User Age", "age") |]
        /// let grid, script = VsHtml.datagrid ("user-grid", values, headers)
        /// </code>
        /// </example>
        static member datagrid(id: string, values: obj array, ?customTitles: (string * string) array) =
            let content = html $"<vscode-data-grid id='{id}'></vscode-data-grid>"
            let titles = defaultArg customTitles Array.empty

            let titles =
                match titles with
                | [||] -> ""
                | values ->
                    let values =
                        values
                        |> Array.map (fun (title, headerKey) ->
                            {| title = title
                               columnDataKey = headerKey |})

                    js $"grid.columnDefinitions = {JS.JSON.stringify values}"

            let script =
                js
                    $"""
                let grid = document.querySelector('#{id}');
                if(grid) {{
                    const data = {JS.JSON.stringify values}
                    grid.rowsData = data;
                    {titles}
                }};
            """

            content, script

    /// <summary>
    /// Helper class to work with vscode webviews
    /// </summary>
    type FsWebview =

        /// <summary>
        /// Creates a new Webview panel with the provided viewType and title
        /// </summary>
        /// <param name="viewType">the viewType string that will be used to identify the webview</param>
        /// <param name="title">the title string that will be used to display the webview</param>
        /// <param name="viewOptions">Where to place the vscode webview</param>
        /// <param name="enableScripts">should this webview run javascript content or not</param>
        /// <param name="resourceRoots">
        /// allow the webview to access certain resources in the local disk,
        /// use an empty array to disallow any access to local resources
        /// </param>
        /// <param name="enableCommandUris">
        /// Commands URIs are links that execute a given command.
        /// They can be used as clickable links in hover text,
        /// completion item details
        /// </param>
        /// <param name="enableFindWidget">Controls if the find widget is enabled in the panel.</param>
        /// <param name="retainContextWhenHidden">Controls if the webview panel's content (iframe) is kept around even when the panel is no longer visible.</param>
        /// <returns>the created webview panel</returns>
        static member create
            (
                viewType: string,
                title: string,
                ?viewOptions,
                ?enableScripts: bool,
                ?resourceRoots: string array,
                ?enableCommandUris: bool,
                ?enableFindWidget: bool,
                ?retainContextWhenHidden: bool
            ) =
            let viewOptions = defaultArg viewOptions (unbox ViewColumn.Beside)

            window.createWebviewPanel (
                viewType,
                title,
                viewOptions,
                {| enableScripts = enableScripts
                   enableCommandUris = enableCommandUris
                   enableFindWidget = enableFindWidget
                   resourceRoots = resourceRoots
                   retainContextWhenHidden = retainContextWhenHidden |}
            )

        /// <summary>
        /// takes an existing webview panel and sets the html content, it takes a list of styles and scripts
        /// that will be added to the webview
        /// </summary>
        /// <remarks>
        /// It uses the extension context to access the extension uri and to set the uri of the vscode ui toolkit
        /// </remarks>
        /// <remarks>
        /// Each script will be wrapped in an iife to ensure it's scope is not polluting other scripts
        /// and located in a script tag at the end of the body of the webview.
        /// These must be JS strings, and should not contain any HTML tags.
        /// </remarks>
        /// <remarks>
        /// Each style will be wrapped by a style tag and put in the head of the webview.
        /// These must be CSS strings, and should not contain any HTML tags.
        /// </remarks>
        /// <param name="ctx">The Extension context</param>
        /// <param name="wbp">The WebViewPanel to re-assign the HTML content</param>
        /// <param name="content">The HTML tags and content to render within the web view</param>
        /// <param name="styles">The css strings for the styles to add to the webview</param>
        /// <param name="scripts">The js strings for the scripts to add to the webview</param>
        static member render
            (
                ctx: ExtensionContext,
                wbp: WebviewPanel,
                content: string,
                ?styles: string seq,
                ?scripts: string seq
            ) =
            let styles = defaultArg styles Seq.empty<string>
            let scripts = defaultArg scripts Seq.empty<string>

            let vsUIWebkit =
                Fable.Import.VSCode.vscode.Uri.joinPath (ctx.extensionUri, "toolkit.min.js")
                |> wbp.webview.asWebviewUri

            wbp.webview.html <-
                html
                    $"""
                    <!DOCTYPE html>
                    <html>
                    <head>
                        %s{mergeStrings styles}
                    </head>
                    <body>
                        %s{content}
                        <script type="module" src="{vsUIWebkit}"></script>
                        %s{mergeScripts scripts}
                    </body>
                    </html>"""

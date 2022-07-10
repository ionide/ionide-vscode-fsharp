namespace Ionide.VSCode.FSharp

open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open global.Node
open Fable.Core.JsInterop

module node = Node.Api

module FSharpLiterate =
    module private Panel =

        let mutable panel: WebviewPanel option = None

        let style =
            """
            /* strings --- and stlyes for other string related formats */
            span.s { color:#E0E268; }
            /* printf formatters */
            span.pf { color:#E0C57F; }
            /* escaped chars */
            span.e { color:#EA8675; }

            /* identifiers --- and styles for more specific identifier types */
            span.id { color:#d1d1d1; }
            /* module */
            span.m { color:#43AEC6; }
            /* reference type */
            span.rt { color:#43AEC6; }
            /* value type */
            span.vt { color:#43AEC6; }
            /* interface */
            span.if{ color:#43AEC6; }
            /* type argument */
            span.ta { color:#43AEC6; }
            /* disposable */
            span.d { color:#43AEC6; }
            /* property */
            span.prop { color:#43AEC6; }
            /* punctuation */
            span.p { color:#43AEC6; }
            /* function */
            span.f { color:#e1e1e1; }
            /* active pattern */
            span.pat { color:#4ec9b0; }
            /* union case */
            span.u { color:#4ec9b0; }
            /* enumeration */
            span.e { color:#4ec9b0; }
            /* keywords */
            span.k { color:#FAB11D; }
            /* comment */
            span.c { color:#808080; }
            /* operators */
            span.o { color:#af75c1; }
            /* numbers */
            span.n { color:#96C71D; }
            /* line number */
            span.l { color:#80b0b0; }
            /* mutable var or ref cell */
            span.v { color:#d1d1d1; font-weight: bold; }
            /* inactive code */
            span.inactive { color:#808080; }
            /* preprocessor */
            span.prep { color:#af75c1; }
            /* fsi output */
            span.fsi { color:#808080; }

            /* omitted */
            span.omitted {
                background:#3c4e52;
            border-radius:5px;
                color:#808080;
                padding:0px 0px 1px 0px;
            }
            /* tool tip */
            div.tip {
                background:#475b5f;
            border-radius:4px;
            font:11pt 'Droid Sans', arial, sans-serif;
                padding:6px 8px 6px 8px;
                display:none;
            color:#d1d1d1;
            pointer-events:none;
            }
            table.pre pre {
            padding:0px;
            margin:0px;
            border:none;
            }
            table.pre, pre.fssnip, pre {
            line-height:13pt;
            border:1px solid #d8d8d8;
            border-collapse:separate;
            white-space:pre;
            font: 9pt 'Droid Sans Mono',consolas,monospace;
            width:90%;
            margin:10px 20px 20px 20px;
            background-color:#212d30;
            padding:10px;
            border-radius:5px;
            color:#d1d1d1;
            max-width: none;
            }
            pre.fssnip code {
            font: 9pt 'Droid Sans Mono',consolas,monospace;
            }
            table.pre pre {
            padding:0px;
            margin:0px;
            border-radius:0px;
            width: 100%;
            }
            table.pre td {
            padding:0px;
            white-space:normal;
            margin:0px;
            }
            table.pre td.lines {
            width:30px;
            }
        """

        let script =
            """
            var currentTip = null;
            var currentTipElement = null;

            function hideTip(evt, name, unique) {
                var el = document.getElementById(name);
                el.style.display = "none";
                currentTip = null;
            }

            function findPos(obj) {
                // no idea why, but it behaves differently in webbrowser component
                if (window.location.search == "?inapp")
                    return [obj.offsetLeft + 10, obj.offsetTop + 30];

                var curleft = 0;
                var curtop = obj.offsetHeight;
                while (obj) {
                    curleft += obj.offsetLeft;
                    curtop += obj.offsetTop;
                    obj = obj.offsetParent;
                };
                return [curleft, curtop];
            }

            function hideUsingEsc(e) {
                if (!e) { e = event; }
                hideTip(e, currentTipElement, currentTip);
            }

            function showTip(evt, name, unique, owner) {
                document.onkeydown = hideUsingEsc;
                if (currentTip == unique) return;
                currentTip = unique;
                currentTipElement = name;

                var pos = findPos(owner ? owner : (evt.srcElement ? evt.srcElement : evt.target));
                var posx = pos[0];
                var posy = pos[1];

                var el = document.getElementById(name);
                var parent = (document.documentElement == null) ? document.body : document.documentElement;
                el.style.position = "absolute";
                el.style.left = posx + "px";
                el.style.top = posy + "px";
                el.style.display = "block";
            }"""

        let setContent str =
            // <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline';">
            panel
            |> Option.iter (fun p ->
                let str =
                    sprintf
                        """
                    <html>
                    <head>

                    <style>
                    pre {
                    color: var(--vscode-editor-foreground);
                    font-family: var(--vscode-editor-font-family);
                    font-weight: var(--vscode-editor-font-weight);
                    font-size: var(--vscode-editor-font-size);
                    }
                    code {
                    font-family: var(--vscode-editor-font-family);
                    font-weight: var(--vscode-editor-font-weight);
                    font-size: var(--vscode-editor-font-size);
                    }
                    %s
                    </style>
                    </head>
                    <body>
                    %s
                    <script>
                    %s
                    </script>
                    </body>
                    </html>
                    """
                        style
                        str
                        script

                p.webview.html <- str)

        let clear () =
            panel
            |> Option.iter (fun p -> p.webview.html <- "")

        let update (textEditor: TextEditor) =
            promise {
                let doc = textEditor.document
                let! res = LanguageService.fsharpLiterate doc.uri
                return setContent res.Data
            }
            |> ignore


        let openPanel () =
            promise {
                match panel with
                | Some p -> p.reveal (!! -2, true)
                | None ->
                    let opts =
                        createObj
                            [ "enableCommandUris" ==> true
                              "enableFindWidget" ==> true
                              "retainContextWhenHidden" ==> true ]

                    let viewOpts =
                        createObj
                            [ "preserveFocus" ==> true
                              "viewColumn" ==> -2 ]

                    let p =
                        window.createWebviewPanel ("fsharpLiterate", "F# Literate", !!viewOpts, opts)

                    let onClose () = panel <- None

                    p.onDidDispose.Invoke(!!onClose) |> ignore
                    panel <- Some p
            }

    let private updatePanel () =
        match Panel.panel with
        | Some _ ->
            let textEditor = window.activeTextEditor.Value

            match textEditor.document with
            | Document.FSharpScript
            | Document.Markdown -> Panel.update textEditor
            | _ -> ()
        | None -> ()

    let private openPanel () =
        match Panel.panel with
        | Some _ -> ()
        | None -> Panel.openPanel () |> ignore

        updatePanel ()

    let fileSaved (event: TextDocument) =
        if event.fileName = window.activeTextEditor.Value.document.fileName then
            updatePanel ()


    let activate (context: ExtensionContext) =
        workspace.onDidSaveTextDocument.Invoke(unbox fileSaved)
        |> context.Subscribe

        window.onDidChangeActiveTextEditor.Invoke(unbox updatePanel)
        |> context.Subscribe

        commands.registerCommand ("fsharp.openFSharpLiterate", openPanel |> objfy2)
        |> context.Subscribe

#Ionide-VSCode: FSharp

Part of the [Ionide](http://ionide.io) plugin suite.
F# IDE-like possibilities in Atom editor and Visual Studio Code

[![Join the chat at https://gitter.im/ionide/ionide-project](https://img.shields.io/badge/gitter-join%20chat%20%E2%86%92-brightgreen.svg?style=flat-square)](https://gitter.im/ionide/ionide-project?utm_source=share-link&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge) --  [Need Help? You can find us on Gitter](https://gitter.im/ionide/ionide-project)

## Features

- Better syntax highlighting
- Auto completions
- Error highlighting and error list
- Tooltips
- Go to Declaration
- Show symbols in file
- Highlighting usages

## Required software

* F# >= 3.1
* MSBuild >= 12

### Windows

This can be obtained by installing Visual Studio 2013 / 2015 or downloading:

* [Visual F# Tools 3.1.2](http://www.microsoft.com/en-us/download/details.aspx?id=44011)
* [Microsoft Build Tools 2013](https://www.microsoft.com/en-us/download/details.aspx?id=40760)

### Mono

* Required: Mono >= 3.10
* Recommended: Mono >= 4.0.2

### PATH settings

* In case of using Mono version, `mono` must be in PATH.
* `Fsi.exe` (or `fsharpi`) must be in PATH

More details how to obtain and install F# on different platforms can be found on http://fsharp.org/

## WebPreview
`WebView` allows the user to override the default conventions used to run and preview web applications. To do so You need to create an `.ionide` file in the root folder of Your project opened by Atom. The configuration file uses the [TOML](https://github.com/toml-lang/toml) language.

Here is the default configuration values used if the `.ionide` file doesn't exist or some entry is missing:

```TOML
[WebPreview]
linuxPrefix = "mono"
command = "packages/FAKE/tools/FAKE.exe"
host = "localhost"
port = 8888
script = "build.fsx"
build = "Serve"
startString = "listener started"
parameters = []
startingPage = ""
```

* linuxPrefix - command used as prefix on Linux / Mac - usually `sh` or `mono`

* command - path to `FAKE.exe`

* host - address of webpage displayed in WebPreview - usually `localhost`

* port - port of webpage displayed in WebPreview (also passed to FAKE as environmental variable)

* script - FAKE build script passed to FAKE - usually `build.fsx`

* build - FAKE build target executed to start WebPreview

* startString - string which needs to be printed out in standard I/O to let know WebPreview to display webpage

* parameters - list of parameters passed to FAKE.exe

* startingPage - webpage displayed in WebPreview - usually ` ` or `index.html`

## Contributing and copyright

The project is hosted on [GitHub](https://github.com/ionide/ionide-vscode-fsharp) where you can [report issues](https://github.com/ionide/ionide-vscode-fsharp/issues), fork
the project and submit pull requests.

The library is available under [MIT license](https://github.com/ionide/ionide-vscode-fsharp/blob/master/LICENSE.md), which allows modification and
redistribution for both commercial and non-commercial purposes.

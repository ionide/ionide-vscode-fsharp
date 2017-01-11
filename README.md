#[Ionide-VSCode: FSharp](https://marketplace.visualstudio.com/items/Ionide.Ionide-fsharp)
**Enhanced F# Language Features for Visual Studio Code**
_Part of the [Ionide](http://ionide.io) plugin suite._

[Need Help? You can find us on Gitter](https://gitter.im/ionide/ionide-project):

[![Join the chat at https://gitter.im/ionide/ionide-project](https://img.shields.io/badge/gitter-join%20chat%20%E2%86%92-brightgreen.svg?style=flat-square)](https://gitter.im/ionide/ionide-project?utm_source=share-link&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)

# Getting Started

F# 4.0 needs to be installed on your system in order to use Ionide

For more detailed instructions on installing F# :

* [Installing F# on Linux](http://fsharp.org/use/linux/)
* [Installing F# on OSX](http://fsharp.org/use/mac/)
* [Installing F# on Windows](http://fsharp.org/use/windows/)


**FSC** _(F# Compiler)_ and **FSI/fsharpi** on Mono _(F# Interactive)_ need to be added to your system **PATH**.
The default location on Windows is
```
  64-bit - C:\Program Files (x86)\Microsoft SDKs\F#\4.0\Framework\v4.0\
  32-bit - C:\Program Files\Microsoft SDKs\F#\4.0\Framework\v4.0
```
## Quick Install Guide

### Windows

[You can download F# 4.0 here for Windows](https://www.microsoft.com/en-us/download/details.aspx?id=48179)

[Install the Microsoft Build Tools 2015](https://www.microsoft.com/en-us/download/details.aspx?id=48159&wa=wsignin1.0)

If you use [chocolatey](https://chocolatey.org/), you can install all the pre-requisites easily:

```batch
choco install windows-sdk-8.0 -y
choco install visualfsharptools -y
choco install microsoft-build-tools -y
choco install visualstudiocode -y
```

### Mono

* Required: Mono >= 3.10
* Recommended: Mono >= 4.0.2

## Features

- Better syntax highlighting
- Auto completions
- Error highlighting and error list
- Tooltips
- Go to Declaration
- Show symbols in file
- Highlighting usages


## WebPreview
`WebView` allows the user to override the default conventions used to run and preview web applications. To do so You need to create an `.ionide` file in the root folder of Your project opened by VSCode. The configuration file uses the [TOML](https://github.com/toml-lang/toml) language.

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

## How to contribute

[![](https://ci.appveyor.com/api/projects/status/5wqf80vub6hqywj8?svg=true)](https://ci.appveyor.com/project/Ionide/ionide-vscode-fsharp)

0. Install nodejs. (On Windows via [chocolaty](https://chocolatey.org/packages/nodejs) this is `C:\> choco install nodejs`)
1. Fork the repo to your GitHub account, then clone locally.
2. Run `build.cmd Build` (or `build.sh Build`). If you have not done so already you may need to configure a GitHub SSH key. 
3. Open folder in VSCode `code .`
4. Make changes
5. Press `Ctrl+Shift+B` to build plugin
5. Press `F5` start experimental instance of VSCode and debugger server
6. Make PR ;)

## How to get logs for debugging / issue reporting

1. Enable Logging in User settings with
  ```json
// FSharp configuration
    // Set the verbosity for F# Language Service Output Channel
    "FSharp.logLanguageServiceRequestsOutputWindowLevel": "DEBUG",

    // Enable logging language service requests (FSAC)  to an output channel, the developer tools console, or both
    "FSharp.logLanguageServiceRequests": "both"
  ```
2. Open the Output Panel and switch to the `F# Language Service` Channel
3. Or Toggle Developer Tools (`Help |> Toggle Developer Tools`) and open the console tab


## Contributing and copyright

The project is hosted on [GitHub](https://github.com/ionide/ionide-vscode-fsharp) where you can [report issues](https://github.com/ionide/ionide-vscode-fsharp/issues), fork
the project and submit pull requests.

The library is available under [MIT license](https://github.com/ionide/ionide-vscode-fsharp/blob/master/LICENSE.md), which allows modification and redistribution for both commercial and non-commercial purposes.

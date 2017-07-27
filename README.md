# [Ionide-VSCode: FSharp](https://marketplace.visualstudio.com/items/Ionide.Ionide-fsharp)
**Enhanced F# Language Features for Visual Studio Code**

_Part of the [Ionide](http://ionide.io) plugin suite._

[![Version](https://vsmarketplacebadge.apphb.com/version/Ionide.Ionide-fsharp.svg)](https://marketplace.visualstudio.com/items?itemName=Ionide.Ionide-fsharp) [![Installs](https://vsmarketplacebadge.apphb.com/installs-short/Ionide.Ionide-fsharp.svg)](https://marketplace.visualstudio.com/items?itemName=Ionide.Ionide-fsharp)
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

Add **MSBUILD_PATH** environment variable which points to the home of **Microsoft Build Tools**, e.g., `MSBUILD_PATH = C:\Program Files (x86)\MSBuild\14.0\Bin`.

## Quick Install Guide

### Windows

[You can download F# 4.0 here for Windows](https://www.microsoft.com/en-us/download/details.aspx?id=48179)

[Install the Microsoft Build Tools 2015](https://www.microsoft.com/en-us/download/details.aspx?id=48159&wa=wsignin1.0)

If you use [chocolatey](https://chocolatey.org/), you can install all the pre-requisites easily:

```batch
choco install windows-sdk-8.0 -y
choco install visualfsharptools -y
choco install microsoft-build-tools --version 14.0.25420.1 -y
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

## How to contribute

0. Install nodejs. (On Windows via [chocolaty](https://chocolatey.org/packages/nodejs) this is `C:\> choco install nodejs`)
1. Run `npm install -g vsce` to install **vsce** globally.
2. Fork the repo to your GitHub account, then clone locally.
3. Run `build.cmd Build` (or `build.sh Build`). If you have not done so already you may need to configure a GitHub SSH key.
4. Open folder in VSCode `code .`
5. Make changes
6. Press `F5` to build plugin and start experimental instance of VSCode
7. Make PR ;)

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

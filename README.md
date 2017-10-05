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
choco install visualfsharptools -y
choco install microsoft-build-tools --version 14.0.25420.1 -y
choco install visualstudiocode -y
```

* Recommended: `dotnet` CLI >= 2.0

### Mono

* Required: Mono >= 4.8
* Recommended: Mono >= 5.2
* Recommended: `dotnet` CLI >= 2.0

## Features

- Syntax highlighting
- Auto completions
- Error highlighting, error list, and quick fixes based on errors
- Tooltips
- Method parameter hints
- Go to Definition
- Peak Definition
- Find all references
- Highlighting usages
- Rename
- Show symbols in file
- Find symbol in workspace
- Show signature in status bar
- Show signature as CodeLens / LineLens
- Go to MSDN help
- Add `open NAMESPACE` for symbol
- Match case generator
- Go to #load reference
- Generate comment for the symbol
- Integration with F# Interactive
- Integration with Forge (Project scaffolding and modification)
- Integration with FSharpLint (additional hints and quick fixes)
- Integration with Expecto (lightweight F# test runner)
- Integration with MsBuild (Build, Rebuild, Clean project)
- Solution / project explorer

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

## How to contribute

*Imposter syndrome disclaimer*: I want your help. No really, I do.

There might be a little voice inside that tells you you're not ready; that you need to do one more tutorial, or learn another framework, or write a few more blog posts before you can help me with this project.

I assure you, that's not the case.

This project has some clear Contribution Guidelines and expectations that you can [read here](https://github.com/ionide/ionide-vscode-fsharp/blob/master/CONTRIBUTING.md).

The contribution guidelines outline the process that you'll need to follow to get a patch merged. By making expectations and process explicit, I hope it will make it easier for you to contribute.

And you don't just have to write code. You can help out by writing documentation, tests, or even by giving feedback about this work. (And yes, that includes giving feedback about the contribution guidelines.)

Thank you for contributing!


## Contributing and copyright

The project is hosted on [GitHub](https://github.com/ionide/ionide-vscode-fsharp) where you can [report issues](https://github.com/ionide/ionide-vscode-fsharp/issues), fork
the project and submit pull requests.

The library is available under [MIT license](https://github.com/ionide/ionide-vscode-fsharp/blob/master/LICENSE.md), which allows modification and redistribution for both commercial and non-commercial purposes.

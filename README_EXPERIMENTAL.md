# [Ionide: Experimental FSharp](https://marketplace.visualstudio.com/items/Ionide.experimental-fsharp)
**Enhanced F# Language Features for Visual Studio Code (EXPERIMENTAL VERSION)**

_Part of the [Ionide](http://ionide.io) plugin suite._ Read detailed documentation at [Ionide docs page](http://ionide.io/docs).

[![Version](https://vsmarketplacebadge.apphb.com/version/Ionide.experimental-fsharp.svg)](https://marketplace.visualstudio.com/items?itemName=Ionide.experimental-fsharp) [![Installs](https://vsmarketplacebadge.apphb.com/downloads-short/Ionide.experimental-fsharp.svg)](https://marketplace.visualstudio.com/items?itemName=Ionide.experimental-fsharp)
[![Rating](https://vsmarketplacebadge.apphb.com/rating-star/Ionide.experimental-fsharp.svg)](https://marketplace.visualstudio.com/items?itemName=Ionide.experimental-fsharp)
[![open collective backers](https://img.shields.io/opencollective/backers/ionide.svg?color=blue)](https://opencollective.com/ionide)
[![open collective sponsors](https://img.shields.io/opencollective/sponsors/ionide.svg?color=blue)](https://opencollective.com/ionide)

You can support Ionide development on [Open Collective](https://opencollective.com/ionide).

[![Open Collective](https://opencollective.com/ionide/donate/button.png?color=blue)](https://opencollective.com/ionide)

# Notes

This is the experimental version of Ionide plugin, used to test new features. Can be unstable.

For normal usage, please use the stable [Ionide-VSCode: FSharp](https://marketplace.visualstudio.com/items/Ionide.Ionide-fsharp) extension instead.

# Requirements

* F# (Windows) - Easiest way to install latest versions of F# on Windows is using [VS Build Tools 2017](https://visualstudio.microsoft.com/downloads/?utm_medium=microsoft&utm_source=docs.microsoft.com&utm_campaign=button+cta&utm_content=download+vs2017#build-tools-for-visual-studio-2017). If you use VS 2017, make sure that you've installed workload adding F# support.

* F# (Linux/MacOS) - F# on non-Windows platform is distributed as part of the `mono`. Installation guide and recent version of `mono` can be found on the [project webpage](https://www.mono-project.com/download/stable/) and on the F# Software Foundation ["Use on Linux" page](https://fsharp.org/use/linux/)

* .NET Core SDK - .NET Core is modern, cross-platform implementation of .NET Framework. Ionide is requiring it for set of features such as project modifications or debugging. The core part of SDK is `dotnet` CLI tool that provides easy way to create, build and run F# projects. What's important - the `dotnet` tool can be used also to create applications targeting also Full Framework (like `net461`). For detailed instructions on installing .NET Core, visit [official step-by-step installation guide](https://www.microsoft.com/net/core).

* VS Code C# plugin (optional) - Ionide's debugging capabilities relies on the debugger provided by Omnisharp team. To get it install [C# extension from VS Code marketplace](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharp)

* MSBuild 2015 (Windows only, optional) - For old, verbose `.fsproj` files on Windows MSBuild 2015 (14.0) needs to be additionally installed. You can download it [here](https://www.microsoft.com/en-us/download/details.aspx?id=48159). However, we highly recommend using new, SDK-based project files.

## Features

- Syntax highlighting
- Auto completions
- Error highlighting, error list, and quick fixes based on errors
- Tooltips
- Method parameter hints
- Go to Definition
- Peek Definition
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
- Integration with MSBuild (Build, Rebuild, Clean project)
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

See https://github.com/ionide/ionide-vscode-fsharp/blob/master/CONTRIBUTING.md

## Contributing and copyright

The project is hosted on [GitHub](https://github.com/ionide/ionide-vscode-fsharp) where you can [report issues](https://github.com/ionide/ionide-vscode-fsharp/issues), fork
the project and submit pull requests.

The library is available under [MIT license](https://github.com/ionide/ionide-vscode-fsharp/blob/master/LICENSE.md), which allows modification and redistribution for both commercial and non-commercial purposes.

Please note that this project is released with a [Contributor Code of Conduct](CODE_OF_CONDUCT.md). By participating in this project you agree to abide by its terms.

## Sponsors

Ionide couldn't be created without support of [Lambda Factory](https://lambdafactory.io). If your company would be interested in supporting development of Ionide, or acquiring commercial support sent us email - lambda_factory@outlook.com

You can also support Ionide development on [Open Collective](https://opencollective.com/ionide).

[![Open Collective](https://opencollective.com/ionide/donate/button.png?color=blue)](https://opencollective.com/ionide)

### Partners

<div align="center">

<a href="https://lambdafactory.io"><img src="https://cdn-images-1.medium.com/max/332/1*la7_YvDFvrtA720P5bYWBQ@2x.png" alt="drawing" width="100"/></a>

</div>

### Sponsors

[Become a sponsor](https://opencollective.com/ionide) and get your logo on our README on Github, description in VSCode marketplace and on [ionide.io](http://ionide.io) with a link to your site.

<div align="center">
<a href="https://opencollective.com/ionide/sponsor/0/website?requireActive=false" target="_blank"><img src="https://opencollective.com/ionide/sponsor/0/avatar.svg?requireActive=false" height="50px"></a>
<a href="https://opencollective.com/ionide/sponsor/1/website?requireActive=false" target="_blank"><img src="https://opencollective.com/ionide/sponsor/1/avatar.svg?requireActive=false" height="50px"></a>
 <a href="https://opencollective.com/ionide/sponsor/2/website?requireActive=false" target="_blank"><img src="https://opencollective.com/ionide/sponsor/2/avatar.svg?requireActive=false" height="50px"></a>
 <a href="http://www.miltonsecurity.com/" target="_blank"><img src="https://opencollective-production.s3-us-west-1.amazonaws.com/dec96cf0-3fd4-11e9-85d3-d3af44ed2d45.jpg" height="50px"></a>
 <a href="https://opencollective.com/ionide/sponsor/3/website?requireActive=false" target="_blank"><img src="https://opencollective.com/ionide/sponsor/3/avatar.svg?requireActive=false" height="50px"></a>

</div>

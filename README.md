# [Ionide-VSCode: FSharp](https://marketplace.visualstudio.com/items/Ionide.Ionide-fsharp)
**Enhanced F# Language Features for Visual Studio Code**

_Part of the [Ionide](https://ionide.io) plugin suite._ Read detailed documentation at [Ionide docs page](https://ionide.io).

[![Version](https://vsmarketplacebadges.dev/version/Ionide.Ionide-fsharp.svg)](https://marketplace.visualstudio.com/items?itemName=Ionide.Ionide-fsharp) [![Installs](https://vsmarketplacebadges.dev/downloads-short/Ionide.Ionide-fsharp.svg)](https://marketplace.visualstudio.com/items?itemName=Ionide.Ionide-fsharp)
[![Rating](https://vsmarketplacebadges.dev/rating-star/Ionide.Ionide-fsharp.svg)](https://marketplace.visualstudio.com/items?itemName=Ionide.Ionide-fsharp)
[![open collective backers](https://img.shields.io/opencollective/backers/ionide.svg?color=blue)](https://opencollective.com/ionide)
[![open collective sponsors](https://img.shields.io/opencollective/sponsors/ionide.svg?color=blue)](https://opencollective.com/ionide)
[![Open in Gitpod](https://img.shields.io/badge/Contribute%20with-Gitpod-908a85?logo=gitpod)](https://gitpod.io/#https://github.com/ionide/ionide-vscode-fsharp)

You can support Ionide development on [Open Collective](https://opencollective.com/ionide).

[![Open Collective](https://opencollective.com/ionide/donate/button.png?color=blue)](https://opencollective.com/ionide)

## Description

Ionide-VSCode is a VSCode plugin that turns VSCode into a fully-fledged IDE for F# development.

The LSP that powers language features is [FSAutoComplete](https://github.com/fsharp/FsAutoComplete).

The library that powers project and script loading is [proj-info](https://github.com/ionide/proj-info)

You find a version of this plugin pre-packaged with the FOSS debugger from Samsung [here](https://open-vsx.org/extension/Ionide/Ionide-fsharp)

# Requirements

* .NET 6.0/7.0 SDK - https://dotnet.microsoft.com/download/dotnet/7.0

* VS Code C# plugin - Ionide's debugging capabilities rely on either the [Omnisharp](https://github.com/OmniSharp/omnisharp-vscode) debugger or [netcoredbg](https://github.com/muhammadsammy/free-omnisharp-vscode).

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
- And more...

## How to Contribute

Ths project is hosted on [GitHub](https://github.com/ionide/ionide-vscode-fsharp) where you can [report issues](https://github.com/ionide/ionide-vscode-fsharp/issues), participate in [discussions](https://github.com/ionide/ionide-vscode-fsharp/discussions), fork
the project and submit pull requests.

### Building and Running

See these instructions for [setting up your local dev environment](https://github.com/ionide/ionide-vscode-fsharp/blob/master/CONTRIBUTING.md#getting-started).

### Guidelines

The [Contribution Guide](https://github.com/ionide/ionide-vscode-fsharp/blob/master/CONTRIBUTING.md) outlines the process and guidelines for getting a patch merged. By making expectations and process explicit, I hope it will make it easier for you to contribute!

### Releasing

* Update `RELEASE_NOTES.md` with the new version number, date (DD.MM.YYYY format please), and brief release notes.
* Push the change to the main branch
* A maintainer can run the `release` workflow from Github's actions page at that point

### Imposter Syndrome Disclaimer

I want your help. *No really, I do*.

There might be a little voice inside that tells you you're not ready; that you need to do one more tutorial, or learn another framework, or write a few more blog posts before you can help me with this project.

I assure you, that's not the case.

And you don't just have to write code. You can help out by writing documentation, tests, or even by giving feedback about this work. (And yes, that includes giving feedback about the contribution guidelines.)

Thank you for contributing!

## Code of Conduct

Please note that this project is released with a [Contributor Code of Conduct](CODE_OF_CONDUCT.md). By participating in this project you agree to abide by its terms.

## Copyright

The library is available under [MIT license](https://github.com/ionide/ionide-vscode-fsharp/blob/master/LICENSE.md), which allows modification and redistribution for both commercial and non-commercial purposes.

## Our Sponsors

Ionide couldn't be created without the support of [Lambda Factory](https://lambdafactory.pl). If your company would be interested in supporting development of Ionide, or acquiring commercial support send us an email - lambda_factory@outlook.com.

You can also support Ionide development on [Open Collective](https://opencollective.com/ionide). 

### Partners

<div align="center">

<a href="https://lambdafactory.pl"><img src="https://cdn-images-1.medium.com/max/332/1*la7_YvDFvrtA720P5bYWBQ@2x.png" alt="drawing" width="100"/></a>

</div>

### Sponsors

[Become a sponsor](https://opencollective.com/ionide) and get your logo on our README on Github, description in the VSCode marketplace and on [ionide.io](https://ionide.io) with a link to your site.

<div align="center">
    <a href="https://ionide.io/sponsors.html">
        <img src="https://opencollective.com/ionide/tiers/silver-sponsor.svg?avatarHeight=120&width=1000&button=false"/>
        <br/>
        <img src="https://opencollective.com/ionide/tiers/bronze-sponsor.svg?avatarHeight=120&width=1000&button=false"/>
    </a>
</div>

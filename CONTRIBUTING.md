# How to contribute

Please take a moment to review this document in order to make the contribution process easy and effective for everyone involved!

For Getting Started instructions, see:

- [**Building** and **Running** Ionide Locally](#getting-started)
- [Submitting **Pull Requests**](#pull-requests)

For issue reporting, use [GitHub Issues](https://github.com/ionide/ionide-vscode-fsharp/issues) and follow the procedures documented here:

- [Submitting **Bug Reports**](#bug-reports)
- [Submitting **Feature Requests**](#feature-requests)

Personal support requests (or help with a contribution) can be discussed on [F# Software Foundation Slack](https://fsharp.org/guides/slack/) or [the F# Discord](https://discord.gg/R6n7c54).

## Getting Started

### Prerequisites

- [Visual Studio Code][vscode] 1.100 or later
- [.NET 10 SDK][dotnet]
- [Node.js][nodejs] 24 or later, which comes with npm

The exact Node.js version that CI uses is in `.tool-versions`, and the .NET SDK version is in `global.json`.

### First-Time Build

1. **Fork the Ionide repo** https://github.com/ionide/ionide-vscode-fsharp on GitHub.
2. **Clone your fork**:
    ```bash
    git clone git@github.com:YOUR_GITHUB_USER/ionide-vscode-fsharp.git
    ```
    or if you don't use ssh:
    ```bash
    git clone https://github.com/YOUR_GITHUB_USER/ionide-vscode-fsharp.git
    ```
3. **Open the repo** in your terminal:
    ```bash
    cd ionide-vscode-fsharp
    ```
4. **Build the project** by running the build script:
    ```bash
    dotnet fsi build.fsx
    ```
5. **Voilà!** You're ready to run Ionide.

**Skip to the [Launching Ionide](#launching-ionide) section** if the build succeeded. Otherwise, check out [Troubleshooting Build Failures](#troubleshooting-build-failures).

### Build Pipelines

`build.fsx` is a [Fun.Build](https://github.com/slaveOftime/Fun.Build) script. Without arguments it runs the `Package` pipeline, the same as CI: it checks the formatting, builds the extension into the `release` folder, and packages it as a `.vsix` in the `temp` folder. Run it *at least once after any clone/pull*, and before you open a pull request.

```bash
dotnet fsi build.fsx
```

On Mac and Linux, `./build.fsx` works too.

When the formatting check fails, format the code with `dotnet fantomas src build.fsx`.

Pick another pipeline with `-p`:

```bash
dotnet fsi build.fsx -p BuildDev
```

- `Package`: the default, what CI runs.
- `Build`: the same build without the formatting check and the `.vsix`, for a quicker local build.
- `BuildDev`: compiles with `DEBUG` defined and an unminified bundle.
- `Watch`: rebuilds on every change, with `DEBUG` defined.
- `Test`: builds the extension and smoke tests it: it downloads VS Code into `.vscode-test`, starts Ionide on `test/smoke/workspace` and waits until FSAC answers a hover. CI runs it after `Package`, and there it tests that build instead of building again. It opens a VS Code window while it runs.

The smoke test is plain JavaScript in `test/smoke`, not F# compiled with Fable like the extension. For a single test, a second Fable project and a compile step before every test run did not seem worth it. When there are more tests, that is worth reconsidering.

`dotnet fsi build.fsx -- -h` lists all pipelines.

To try a build in your everyday VS Code instead of the Extension Development Host, install the package from the `temp` folder:

```bash
code --install-extension temp/Ionide-fsharp-<version>.vsix
```

### Troubleshooting Build Failures

- **Paket fails to restore `ionide-fsgrammar`.** Paket 11 and later fail to create the git cache for a git dependency on a machine that never restored it before ([fsprojects/Paket#4461](https://github.com/fsprojects/Paket/issues/4461)). Seed the cache once, then build again:
    ```bash
    git clone --mirror https://github.com/ionide/ionide-fsgrammar.git ~/.paket/git/db/ionide-fsgrammar
    ```
    On Windows, clone into `%USERPROFILE%\.paket\git\db\ionide-fsgrammar`.
- **`npm install` warns about engines.** Check `node --version`. The build needs Node.js 24 or later.
- **The formatting check fails.** Run `dotnet fantomas src build.fsx` and build again.

### Launching Ionide

When you're ready to test out some code changes, use VS Code's [Run View](https://code.visualstudio.com/Docs/editor/debugging#_run-view) to build and launch Ionide in a new [Extension Development Host](https://code.visualstudio.com/api/get-started/your-first-extension), a separate "dev mode" instance of VS Code used for developing extensions.

First, open the Ionide folder in VS Code:

```bash
cd ionide-vscode-fsharp
code .
```

Then open Ionide in a new Extension Development Host with one of the following methods.

#### `Build and Launch Extension`

This builds the extension (the `Build` pipeline) and launches it in a new Extension Development Host.

1. Open the "Run and Debug" view (`cmd`+`shift`+`d` or `ctrl`+`shift`+`d`).
2. Choose the `Build and Launch Extension` launch configuration.
3. Run it by pressing `F5`.

`Build (dev) and Launch Extension` does the same with the `BuildDev` pipeline, which is easier to debug.

#### Watch Mode (Recommended)

There are **two steps** involved in running Ionide in Watch Mode.

TL;DR: watch this video clip:

https://user-images.githubusercontent.com/3221269/208322748-401bc978-1f43-4098-a812-78e0c7f93e18.mp4

1. **Start the `Watch` task**.
    - Open the command palette (`cmd`+`shift`+`p` or `ctrl`+`shift`+`p`).
    - Enter `Tasks: Run Task`.
    - Enter `Watch`.
    - **Wait a few seconds**. The initial build can take 15-30 seconds.
2. **Run the `Launch Only` launch configuration**.
    - Open the "Run and Debug" view (`cmd`+`shift`+`d` or `ctrl`+`shift`+`d`).
    - Choose the `Launch Only` configuration.
    - Run it by pressing `F5`.

Step 1 starts a `Watch` task that rebuilds Ionide in the background while you change the source code. Wait for its first build to finish before you open the Extension Development Host. To see what it is doing, run `Tasks: Show Running Tasks` from the command palette.

### Working with FSAC (FsAutoComplete)

Most Ionide bugs are actually in FsAutoComplete (FSAC), the F# language server. This repo holds the glue code between FSAC and VS Code. The behaviour you see when you use Ionide is mostly implemented in FSAC.

1. **Fork the FSAC repo** https://github.com/ionide/FsAutoComplete on GitHub.
2. **Clone your fork**:
    ```bash
    git clone git@github.com:YOUR_GITHUB_USER/FsAutoComplete.git
    ```
    or if you don't use ssh:
    ```bash
    git clone https://github.com/YOUR_GITHUB_USER/FsAutoComplete.git
    ```
3. **Build FSAC** as its [CONTRIBUTING.md](https://github.com/ionide/FsAutoComplete/blob/main/CONTRIBUTING.md) describes, and copy the path of the build output. It is a path inside the FSAC repo that ends with `src/FsAutoComplete/bin/Release/net10.0/fsautocomplete.dll`. You can also leave out `net10.0/fsautocomplete.dll`, Ionide then picks the matching target framework itself. `Release` is `Debug` when you build that configuration.
4. In the VS Code instance that has the Ionide repo open, open the settings (`cmd`+`,` or `ctrl`+`,`), find `FSharp > Fsac: Net Core Dll Path` (`FSharp.fsac.netCoreDllPath`) and paste the path from step 3.
5. Find `FSharp > Fsac: Attach Debugger` (`FSharp.fsac.attachDebugger`) and check the box.
6. In the "Run and Debug" view, run `Build and Launch Extension`. After a while a second VS Code instance starts, which you can use to test Ionide with your FSAC.
7. To attach the debugger, go to the VS Code instance that has the FSAC repo open, and in the "Run and Debug" view, run `.NET Core Attach`. Choose the dotnet process that shows `fsautocomplete.dll --mode lsp --attachdebugger`.
8. Breakpoints in the FSAC solution now hit for the instance from step 6.

There is a [video](https://www.youtube.com/watch?v=w36_PvHNoPY) that goes through these steps and fixes a bug in a little more detail.

Remove the settings from steps 4 and 5 to go back to the FSAC that ships with Ionide.

## Pull requests

Good pull requests (patches, improvements, new features) are a fantastic help. They should remain focused in scope and avoid containing unrelated commits.

**IMPORTANT**: By submitting a patch, you agree that your work will be licensed under the license used by the project.

If you have a large pull request in mind (e.g. implementing features, refactoring code, etc.), **please ask first**. Otherwise you risk spending a lot of time on something the project's developers might not want to merge.

Please adhere to the coding conventions in the project (formatting, accurate comments, etc.). Before you open the pull request, run `dotnet fsi build.fsx`: CI runs the same `Package` pipeline on Linux, Windows and macOS, and fails on unformatted code. CI then runs the `Test` pipeline, which you can run with `dotnet fsi build.fsx -p Test`.

## Bug reports

A bug is either a _demonstrable problem_ that is caused by Ionide failing to provide the expected feature, or missing, unclear, or misleading documentation. Good bug reports are extremely helpful - thank you!

Guidelines for bug reports:

1. **Use the GitHub issue search**: check if the issue has already been reported.
2. **Check if the issue has been fixed**: try to reproduce it using the `main` branch of the repository.
3. **Isolate and report the problem**: ideally create a reduced test case.

Please be as detailed as possible in your report. Include your operating system and your `dotnet --version`, the steps to reproduce the issue, and the outcome you expected. All these details help developers to fix the bug.

Ionide gathers most of this information for you: run `F#: Get info for diagnostics` from the command palette. It opens a file like this:

> <!-- Please copy/paste this file content into a Github issue -->
> ### Problem
>
> <!-- Describe here your problem -->
>
> ### Steps to reproduce
>
> <!-- Add here the steps to reproduce you problem. Example: -->
> <!-- 1. Open an F# file -->
> <!-- 2. Ctrl + P > "F# Add Reference" -->
>
> ### Machine infos
>
> * Operating system: **Darwin**
> * Arch: **arm64**
> * VSCode: **1.100.0**
> * UI Kind: **Desktop**
> * Ionide: **8.0.1**

Copy and paste this file into the issue on GitHub and fill the gaps. You can leave the lines that start with `<!--` in place, GitHub does not display them.

## Feature requests

Feature requests are welcome and should be discussed on the issue tracker. But take a moment to find out whether your idea fits with the scope and aims of the project. It's up to *you* to make a strong case to convince the community of the merits of this feature. Please provide as much detail and context as possible.

## Release

1. Dependencies
    1. If updating FSAC
        1. Run `dotnet paket update --group fsac`.
        2. Commit the `paket.lock`.
    2. If updating ionide-fsgrammar
        1. Open `paket.lock`.
        2. Find the line `remote: https://github.com/ionide/ionide-fsgrammar.git`.
        3. Update the hash below it to the latest commit on the main branch.
        4. Commit the `paket.lock`.
    3. If updating ionide-vscode-helpers
        1. Open `paket.dependencies`.
        2. Find the lines for `github ionide/ionide-vscode-helpers`.
        3. Update the hash for all the files.
        4. Run `dotnet paket install`.
        5. Commit the `paket.dependencies` and `paket.lock`.
2. Add the version and notes at the top of `RELEASE_NOTES.md`, under a heading like `### 8.0.2 - 24.10.2026` (the date is optional).
    1. If possible, link the pull request of each change and mention its author.
    2. If updating FSAC, mention the FSAC version and link its release notes.

    The build reads the version from the first heading. It fails when a `###` heading that is not a release, such as `### Unreleased`, sits above it.
3. Commit `RELEASE_NOTES.md` and push it to `main` of https://github.com/ionide/ionide-vscode-fsharp.
4. [Go to the Release workflow on GitHub Actions](https://github.com/ionide/ionide-vscode-fsharp/actions/workflows/release.yml).
    1. Click `Run workflow` in the upper right, with `main` as the branch.
    2. Click the `Run workflow` button in the dialog that pops up.

The workflow runs the `Package` pipeline, then the `Release` pipeline: it commits the version bump in `release/package.json`, tags it, creates the GitHub release with the `.vsix`, and publishes that `.vsix` to the VS Code Marketplace. Last, it publishes to the Open VSX Registry.

[dotnet]: https://dotnet.microsoft.com/download/dotnet/10.0
[nodejs]: https://nodejs.org/en/download/
[vscode]: https://code.visualstudio.com/Download

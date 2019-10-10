# How to contribute

Please take a moment to review this document in order to make the contribution process easy and effective for everyone involved!

## Using the issue tracker

Use the issues tracker for:

* [bug reports](#bug-reports)
* [feature requests](#feature-requests)
* [submitting pull requests](#pull-requests)

Personal support request should be discussed on [F# Software Foundation Slack](https://fsharp.org/guides/slack/).

## Bug reports

A bug is either a _demonstrable problem_ that is caused in Ionide failing to provide the expected feature or indicate missing, unclear, or misleading documentation. Good bug reports are extremely helpful - thank you!

Guidelines for bug reports:

1. **Use the GitHub issue search** &mdash; check if the issue has already been reported.

2. **Check if the issue has been fixed** &mdash; try to reproduce it using the `master` branch in the repository.

3. **Isolate and report the problem** &mdash; ideally create a reduced test case.

Please try to be as detailed as possible in your report. Include information about
your Operating System, as well as your `dotnet` (or `mono` \ .NET Framework), and F# versions. Please provide steps to
reproduce the issue as well as the outcome you were expecting! All these details
will help developers to fix any potential bugs.

Ionide provide an easy way to gather all this information:

Do `Ctrl+Shift+P > F#: Get info for diagnostics`, this will open a file with something like:

> <!-- Please copy/paste this file content into a Github issue -->
> ### Problem
>
> <!-- Describe here your problem -->
>
> ### Steps to reproduce
>
> <!-- Add here the step to reproduce you problem. Example: -->
> <!-- 1. Open an F# file -->
> <!-- 2. Ctrl + P > "F# Add Reference" -->
>
> ### Machine infos
>
> * Operating system: **Darwin**
> * Arch: **x64**
> * VSCode: **1.23.1**
> * Runtime: **netcore**
> * Dotnet version: **2.1.103**
> <!-- You can also linked the FSAC log file into your issue -->
> <!-- Use `Ctrl+P > "F#: Get FSAC logs"` commands to get file location -->

Now, you can copy/paste this file in the issue on github and fill the gaps. You can let the lines started by `<!--` they will not be displayed by github.

## Feature requests

Feature requests are welcome and should be discussed on issue tracker. But take a moment to find
out whether your idea fits with the scope and aims of the project. It's up to *you*
to make a strong case to convince the community of the merits of this feature.
Please provide as much detail and context as possible.

## Pull requests

Good pull requests - patches, improvements, new features - are a fantastic
help. They should remain focused in scope and avoid containing unrelated
commits.

**IMPORTANT**: By submitting a patch, you agree that your work will be
licensed under the license used by the project.

If you have any large pull request in mind (e.g. implementing features,
refactoring code, etc), **please ask first** otherwise you risk spending
a lot of time working on something that the project's developers might
not want to merge into the project.

Please adhere to the coding conventions in the project (indentation,
accurate comments, etc.).

## How to build and test a local version of Ionide

### Prerequisites

- [Visual Studio Code][vscode] 🙄
- [Mono][mono]
- [.NET Core 2.0][dotnet]
- [Node.js][nodejs]
- [Yarn][yarn]
- [MSBuildTools2015][msbuildtools2015]

### Building

Fork, from the github interface https://github.com/ionide/ionide-vscode-fsharp
 - if you don't use a certificate for committing to github:
```bash
git clone https://github.com/YOUR_GITHUB_USER/ionide-vscode-fsharp.git
```
 - if you use a certificate for github authentication:
```bash
git clone git@github.com:YOUR_GITHUB_USER/ionide-vscode-fsharp.git
```

#### First time build:
```bash
cd ionide-vscode-fsharp
./build.sh  # or build.cmd if your OS is Windows  (might need ./build Build here)
```

If `dotnet restore` gives the error ` The tools version "14.0" is unrecognized`, then you need to install [msbuildtools2015][msbuildtools2015]

If `dotnet restore` gives the error `error MSB4126: The specified solution configuration "Debug|x64" is invalid`, there's a good chance you have the `Platform` environment variable set to "x64".  Unset the variable and try the restore command again.

If `./build.sh` gives errors, you may need to run `./build.sh Build` one time.


Everything is done via `build.cmd` \ `build.sh`.

- `build Build` does a full-build, including package installation and copying some necessary files.<br/>
  It should always be done at least once after any clone/pull.
- If a git dependency fails to build paket won't re-do it you can run their build scripts manually:
  - In `paket-files\github.com\fsharp\FsAutoComplete` run `build LocalRelease`
  - In `paket-files\github.com\fsharp-editing\Forge` run `build Build`

### Launching the extension

Once the initial build on the command line is completed, you should use vscode itself to build and launch the development extension.   To do this,

- open the project folder in vscode
- Use one of the following two configurations which will build the project and launch a new vscode instance running your vscode extension
- In VSCode two configurations are possible to run:
  - Use `Build and Launch Extension`
  - Start the `Watch` task and when a build is done start `Launch Only`

These two options can be reached in VsCode in the side bar (look for a Beetle symbol), or by typing `control-P Debug <space> ` and then selecting either `Build and Launch` or `Watch`

The new extension window will appear with window title `Extension development host`

### Working with FSAC

1. Open FSAC from a new instance of VSCode from the directory: `paket-files/github.com/fsharp/FsAutoComplete`
2. Build the FSAC solution and copy the dll output from the output log, it should be something like: `paket-files/github.com/fsharp/FsAutoComplete/src/FsAutoComplete/bin/Debug/netcoreapp2.1/fsautocomplete.dll`.  Nore `netcoreapp2.1` may be a different version.
3. In the instance of VSCode that you have Ionide open, open settings (`CMD ,` or `Ctrl ,`), and find the section `FSharp > Fsac: Net Core Dll Path` and paste the output you copied from step 3.
4. Now find the section `FSharp > Fsac: Attach Debugger` and check the check box.
5. Close settings
6. Goto the debug section and hit `Build and Launch extension`, after a while another instance of VSCode will start, you can use this instance to test Ionide/FsAutoComplete.
7. To attach the debugger go back to the instance of VSCode where you open FSAC and goto the debug section, hit `.NET Core Attach` in the list shown you should see all the dotnet processes running, choose one that has `fsautocomplete.dll --mode lsp --attachdebugger` shown.
8. Now you will be able to use breakpoints in the FsAutocomplete solution to debug the instance from step 6.

There is a video [here](https://www.youtube.com/watch?v=w36_PvHNoPY) that goes through the steps and fixing a bug in a little more detail.

Remove the settings from steps 3 and 4 to go back to FSAC bundled in Ionide extension.

### Dependencies

[dotnet]: https://www.microsoft.com/net/download/core
[mono]: http://www.mono-project.com/download/
[nodejs]: https://nodejs.org/en/download/
[yarn]: https://yarnpkg.com/en/docs/install
[vscode]: https://code.visualstudio.com/Download
[msbuildtools2015]: https://www.microsoft.com/en-us/download/details.aspx?id=48159&wa=wsignin1.0

# How to contribute

Ionide is primarily maintained through agentic development under human guidance. Maintainers discuss
proposed work and then direct coding agents to make the complete change, including implementation,
tests, documentation, and other affected files.

## Start With an Issue

We generally prefer contributions as [GitHub issues] rather than pull requests. Search for an
existing report first and follow the [bug report](#bug-reports) or
[feature request](#feature-requests) guidance below. Issues may include proposed changes, patches, or
links to forks or branches. Maintainers may refine the scope and assign the issue to an agent to
implement and validate the complete change.

## Repo Assist

[Repo Assist] is an automated AI assistant that runs regularly in this repository. It may triage or
respond to issues, investigate bugs, suggest improvements, and attempt implementations as draft pull
requests. Its work is identified as automated and remains subject to human review; Repo Assist does
not merge pull requests or make final maintenance decisions.

Maintainers can invoke Repo Assist with `/repo-assist <instructions>` for a specific agentic task,
such as investigating an issue, preparing a fix, adding tests, or updating documentation.

Personal support request (or getting contribution help) should be discussed on [F# Software Foundation Slack](https://fsharp.org/guides/slack/) or [the F# Discord](https://discord.gg/R6n7c54).

## Getting Started

First things first! In order to build & develop Ionide locally, you'll need to install the following.

### Prerequisites

- [Visual Studio Code][vscode]
- [.NET 7][dotnet]
- [Node.js][nodejs]
- [Yarn][yarn]

### First-Time Build

1. **Fork the Ionide repo** https://github.com/ionide/ionide-vscode-fsharp on github.

1. **Clone your fork**:
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
3. Then **build the project** by running the build script:
    (on Mac/Linux)
    ```bash
    ./build.sh
    ```
    (or on Windows)
    ```bash
    .\build.cmd
    ```
4. **Voilà!** You're ready to run Ionide.

**Skip to the [Launching Ionide](#launching-ionide) section** if the build succeeded.

Otherwise, check out the [Troubleshooting Build Failures](#troubleshooting-build-failures) guide.

### Running a Full Build

This should *always* be done __at least once after any clone/pull__.

```bash
./build.sh -t Build
```
(or on Windows)
```bash
.\build.cmd -t Build
```

This will re-install any necesssary packages and files.

### Troubleshooting Build Failures

- If the `FsAutoComplete` dependency fails to build, you can re-run it manually:
  ```bash
    # browse to the FsAutoComplete source code folder
    cd paket-files/github.com/fsharp/FsAutoComplete
    # build FsAutoComplete (Mac/Linux)
    ./build.sh LocalRelease
    # build FsAutoComplete (Windows)
    .\build.cmd LocalRelease
  ```

- If `dotnet restore` gives the error ` The tools version "14.0" is unrecognized`, then you need to install [msbuildtools2015][msbuildtools2015]

- If `dotnet restore` gives the error `error MSB4126: The specified solution configuration "Debug|x64" is invalid`, there's a good chance you have the `Platform` environment variable set to "x64".  Unset the variable and try the restore command again.

- If `./build.sh` gives errors, you may need to run `./build.sh -t Build` one time.


### Launching Ionide

When you're ready to test out some code changes, you can use vscode's [Run View](https://code.visualstudio.com/Docs/editor/debugging#_run-view) to build and launch Ionide in a new  [Extension Development Host instance](https://code.visualstudio.com/api/get-started/your-first-extension), which is a separate "dev mode" instance of VS code used for developing extensions.

First, open the Ionide project folder in vscode.
  ```bash
  cd ionide-vscode-fsharp # or whereever you cloned the ionide repo.
  code .
  ```
Then, you can open Ionide in a new `Extension Development Host` using one of the following methods:

#### `Build and Launch Extension`

This builds the extension and launches it immediately in a new vscode extension host.

1. Open the "Run and Debug" menu (`cmd`+`shift`+`d` or `ctrl`+`shift`+`d`).
2. Choose the `Build and Launch Extension` launch configuration
3. Run it by pressing `F5`.

#### Watch Mode (Recommended)

There are **two steps** involved in running Ionide in Watch Mode.

TL;DR: watch this video clip:

https://user-images.githubusercontent.com/3221269/208322748-401bc978-1f43-4098-a812-78e0c7f93e18.mp4

1. **Start the `Watch` task**.
    - Open the command palette (`cmd`+`shift`+`p` or `ctrl`+`shift`+`p`)
    - Enter `tasks: run tasks`.
    - Enter `watch`.
    - **Wait a few seconds**. It might take 15-30 seconds for the initial build to complete
2. **Run the `Launch Only` launch configuration**.
    - Open the Run and Debug menu (`cmd`+`shift`+`d` or `ctrl`+`shift`+`d`)
    - Choose the `Launch Only` configuration
    - Run it by pressing `F5`.

Note: Step 1 starts a new "Watch" task that rebuilds Ionide in the background while you're making source code changes. You should wait for this to finish before opening the Extension Host.

If you're curious what's going on, you can view the log output for this task by running `Tasks: Show Running Tasks` from the command palette.

### Working with FSAC (FSharpAutoComplete)
Most Ionide bugs are actually from there. This repo only stores the glue code between FSAC and Visual Studio Code - not the actual implementation of behaviours you would experience when using Ionide in Visual Studio Code which are implemented in FSAC.
1. **Fork the FSAC repo** https://github.com/fsharp/FsAutoComplete on github.

1. **Clone your fork**:
    ```bash
    git clone git@github.com:YOUR_GITHUB_USER/FsAutoComplete.git
    ```
    or if you don't use ssh:

    ```bash
    git clone https://github.com/YOUR_GITHUB_USER/FsAutoComplete.git
    ```
2. Follow the [FSAC build instructions](https://github.com/fsharp/FsAutoComplete#building-and-testing) and copy the dll output from the output log, it should be a path inside the FSAC repo ending with `src/FsAutoComplete/bin/Release/net6.0/fsautocomplete.dll`. (Alternatively, delete the `net6.0/fsautocomplete.dll` part and it will also work.) Note that `Release` may be substituted with `Debug` if you build with that configuration instead. Also, note that `net6.0` may be replaced with another .NET version like `net7.0` as FsAutoComplete targets a newer .NET version.
3. In the instance of VSCode that you have Ionide open, open settings (`CMD ,` or `Ctrl ,` by default), and find the section `FSharp > Fsac: Net Core Dll Path` and paste the output you copied from step 3.
4. Now find the section `FSharp > Fsac: Attach Debugger` and check the check box.
5. Close settings
6. Goto the debug section and hit `Build and Launch extension`, after a while another instance of VSCode will start, you can use this instance to test Ionide/FsAutoComplete.
7. To attach the debugger go back to the instance of VSCode where you open FSAC and goto the debug section, hit `.NET Core Attach` in the list shown you should see all the dotnet processes running, choose one that has `fsautocomplete.dll --mode lsp --attachdebugger` shown.
8. Now you will be able to use breakpoints in the FsAutocomplete solution to debug the instance from step 6.

There is a video [here](https://www.youtube.com/watch?v=w36_PvHNoPY) that goes through the steps and fixing a bug in a little more detail.

Remove the settings from steps 3 and 4 to go back to FSAC bundled in Ionide extension.

### FSAC Dependencies

- [.NET 6][dotnet]
- [Node.js][nodejs]
- [Yarn][yarn]
- [Visual Studio Code][vscode]

## Pull requests

Every pull request must have a matching issue that has been discussed with the maintainers. Link the
pull request to that issue, keep it focused in scope, and avoid unrelated commits.

**IMPORTANT**: By submitting a patch, you agree that your work will be
licensed under the license used by the project.

Maintainers may close a pull request and use the issue as the basis for an agent-produced
implementation instead. The submitted analysis and code remain valuable inputs to that work.

Please adhere to the coding conventions in the project (indentation,
accurate comments, etc.).

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


[dotnet]: https://dotnet.microsoft.com/en-us/download/dotnet/7.0
[nodejs]: https://nodejs.org/en/download/
[yarn]: https://yarnpkg.com/en/docs/install
[vscode]: https://code.visualstudio.com/Download
[GitHub issues]: https://github.com/ionide/ionide-vscode-fsharp/issues
[Repo Assist]: https://github.com/githubnext/agentics/blob/main/docs/repo-assist.md


## Release

1. Dependencies
    1. If updating FSAC
        1. run `dotnet paket update --group fsac`
        2. Commit the `paket.lock` 
    2. If updating ionide-grammar
        1. `open paket.lock`
        2. find the line `remote: https://github.com/ionide/ionide-fsgrammar.git`
        3. update the hash to the latest commit on the main branch
        4. Commit the `paket.lock` 
    3. if updating ionide-vscode-helpers
        1. open `paket.dependencies`
        2. find the lines for `github ionide/ionide-vscode-helpers`
        3. Update the hash for all the files
        4. run `dotnet paket install`
        5. Commit the `paket.dependencies` and `paket.lock`
2. Add version and notes to `RELEASE_NOTES.md`
    1. If possible link the pull request of the changes and mention the author of the pull request
    2. If updating FSAC mention the version of FSAC that is being updated to and link it's release notes
3. Commit `RELEASE_NOTES.md`
    1. `git commit -m "Release notes for x.x.x"`
    2. `git push [remote-branch] main`
4. [Goto the Release workflow on Github Action](https://github.com/ionide/ionide-vscode-fsharp/actions/workflows/release.yml)
    1. Click `Run workflow` in the upper right
    2. Click `Run workflow` button on the dialog that pops up

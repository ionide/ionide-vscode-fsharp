# How to contribute

Please take a moment to review this document in order to make the contribution process easy and effective for everyone involved!

For Getting Started instructions, see:

- [**Building** and **Running** Ionide Locally](#getting-started)
- [Submitting **Pull Requests**](#pull-requests)

For issue reporting, use [Github Issues](https://github.com/ionide/ionide-vscode-fsharp/issues) and follow the procedures documented here:

- [Submitting **Bug Reports**](#bug-reports)
- [Submitting **Feature Requests**](#feature-requests)

Personal support request should be discussed on [F# Software Foundation Slack](https://fsharp.org/guides/slack/).

## Getting Started 

First things first! In order to build & develop Ionide locally, you'll need to install the following.

### Prerequisites

- [Visual Studio Code][vscode] 🙄
- [.NET Core 3.1][dotnet]
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
    build.cmd # (might need ./build Build instead here.)
    ```
4. **Voilà!** You're ready to run Ionide.

**Skip to the [Launching Ionide](#launching-ionide) section** if the build succeeded.

Otherwise, check out the [Troubleshooting Build Failures](#troubleshooting-build-failures) guide.

### Running a Full Build

This should *always* be done __at least once after any clone/pull__.  

```bash
./build.sh -t Build
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
    build.cmd LocalRelease
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

https://user-images.githubusercontent.com/2154029/143313128-94b7b5ce-64d3-4008-97ac-ff44fdce58cc.mp4

1. **Start the `Watch` task**.
    - Open the command palette (`cmd`+`p` or `ctl`+`p`)
    - Enter `tasks watch`.  
    - **Wait a few seconds**. It might take 15-30 seconds for the initial build to complete
2. **Run the `Launch Only` launch configuration**.
    - Open the Run and Debug menu (`cmd`+`shift`+`d` or `ctrl`+`shift`+`d`)
    - Choose the `Launch Only` configuration
    - Run it by pressing `F5`.

Note: Step 1 starts a new "Watch" task that rebuilds Ionide in the background while you're making source code changes. You should wait for this to finish before opening the Extension Host.

If you're curious what's going on, you can view the log output for this task by running `Tasks: Show Running Tasks` from the command palete. 

### Working with FSAC

1. Open FSAC from a new instance of VSCode from the directory: `paket-files/github.com/fsharp/FsAutoComplete`
2. Build the FSAC solution and copy the dll output from the output log, it should be something like: `paket-files/github.com/fsharp/FsAutoComplete/src/FsAutoComplete/bin/Debug/net5.0/fsautocomplete.dll`.
3. In the instance of VSCode that you have Ionide open, open settings (`CMD ,` or `Ctrl ,`), and find the section `FSharp > Fsac: Net Core Dll Path` and paste the output you copied from step 3.
4. Now find the section `FSharp > Fsac: Attach Debugger` and check the check box.
5. Close settings
6. Goto the debug section and hit `Build and Launch extension`, after a while another instance of VSCode will start, you can use this instance to test Ionide/FsAutoComplete.
7. To attach the debugger go back to the instance of VSCode where you open FSAC and goto the debug section, hit `.NET Core Attach` in the list shown you should see all the dotnet processes running, choose one that has `fsautocomplete.dll --mode lsp --attachdebugger` shown.
8. Now you will be able to use breakpoints in the FsAutocomplete solution to debug the instance from step 6.

There is a video [here](https://www.youtube.com/watch?v=w36_PvHNoPY) that goes through the steps and fixing a bug in a little more detail.

Remove the settings from steps 3 and 4 to go back to FSAC bundled in Ionide extension.

### FSAC Dependencies

[dotnet]: https://www.microsoft.com/net/download/core
[nodejs]: https://nodejs.org/en/download/
[yarn]: https://yarnpkg.com/en/docs/install
[vscode]: https://code.visualstudio.com/Download

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

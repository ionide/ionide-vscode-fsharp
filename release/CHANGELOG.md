### 5.12.0 - 13.03.2022

* Update FSAC to 0.51.0 to pick up `textDocument/signatureHelp` fixes and a new CodeFix for converting DU match cases from positional to named patterns.

### 5.11.2 - 12.03.2022

* Update FSAC to 0.50.1 to pick up completion fixes and diagnostics fixes

### 5.11.1 - 6.04.2022

* Update Ionide.FsGrammar to get fixes for multiline comment tokenization
* Better error reporting in the initial extension startup pipeline
* Update Fable and supporting libraries

### 5.11.0 - 16.02.2022

* Update FSAC to version 0.50.0 to get fixes
  * Changed
    * Update Fantomas.Client to prefer stable versions (Thanks @nojaf)
    * Moved to use the Ionide.LanguageServerProtocol shared nuget package

  * Fixed
    * Sourcelink's go-to-definition works better on windows for deterministic paths
    * Fix missing commas in Info Panel generic type signatures (Thanks @jcmrva!)
    * Fix off-by-1 error in the negation-to-subtraction codefix (Thanks @jasiozet!)
* Add config setting for server trace so that we have better autocomplete for that when requesting logs.
* Clean up and fix Ionide diagnostics

### 5.10.2 - 2.12.2021

* Update FSAC to version 0.49.5 to get fixes for
  * Fantomas.Client updates
  * Background services caches
* Enable the Info Panel feature for F# scripts
* Update the embedded grammar file

### 5.10.1 - 20.11.2021
* Update FSAC to version 0.49.4 to get fixes for:
  * Fix regression in cross-project support after FCS 40 update in `proj-info`
  * Better handling of file typechecking after FCS 40 update
  * Fix background service
  * Fix File System

### 5.9.1 - 14.11.2021

* Update FSAC to version 0.49.1 to get fixes for
  * code lenses displays

### 5.9.0 - 8.11.2021

* Update FSAC to version 0.49.0 to get support for .Net 6 and F# 6
* [Change the smart-indent support to allow users to delete indents one level at a time](https://github.com/ionide/ionide-vscode-fsharp/pull/1609) (thanks @Tasiro!)

### 5.8.1 - 24.10.2021

* Update FSAC to version 0.48.1 to get fixes for
  * Fantomas.Client integration

### 5.8.0 - 23.10.2021

* [Fix icons for solutions, projects, and references](https://github.com/ionide/ionide-vscode-fsharp/pull/1593) (thanks @sharno!)
* Update FSAC to version 0.48.0 to get fixes for
  * xmldoc handling in tooltips
  * using user-specified fantomas instead of a bundled version

### 5.7.3 - 10.09.2021

* [Fix bug in windows CLI calls](https://github.com/ionide/ionide-vscode-fsharp/pull/1579) (thanks @alfonsogarciacaro)

### 5.7.2 - 09.09.2021

* Update Fable dependencies
* Update FSAC to version 0.47.2 to get fixes for
  * dotnet new template parsing
  * Don't provide completions or tooltips for string literals of all kinds
    * this enables better integrations with html/sql/css extensions powered by then [Highlight HTML/SQL templates in F#](https://marketplace.visualstudio.com/items?itemName=alfonsogarciacaro.vscode-template-fsharp-highlight) extension.

### 5.7.1 - 05.08.2021

* Fix terminal initialization failure introduced in 5.7.0

### 5.7.0 - 04.08.2021

* Update FSAC to version 0.47.1 to get fixes for
  * error handling around Fantomas formatting
* automatically run FSAC under the .net 6 runtime if the project supports .net 6
  * either a global.json that declares .Net 6 SDKs, or
  * no global.json and a latest installed version of .Net 6

### 5.6.0 - 26.07.2021

* Update FSAC to version 0.47.0 to get fixes for
  * allowing to run on .net 6
  * 'dotnet new' template loading
  * Fantomas update to 4.5.0
  * 'workspace/applyEdit' data type fixes
* remove a bunch of legacy mono config options that were no longer being used

### 5.5.9 - 29.06.2021

* BUGFIX: republish 5.5.8 with lockfile additions so that FSAC is actually updated

### 5.5.8 - 29.06.2021

* Update FSAC to version 0.46.7 to get fixes for
  * RemoveUnusedBinding codefix
  * UnusedValue codefix
  * 'textDocument/codeAction' parameter values

### 5.5.7 - 21.06.2021

* [Fix parsing of multiple log category filters](https://github.com/ionide/ionide-vscode-fsharp/pull/1541) (thanks @Booksbaum)
* Update FSAC to version 0.46.5 to get fixes for
  * better diagnostics reporting with more error codes and help links

### 5.5.6 - 18.06.2021

* Update FSAC to version 0.46.4 to get fixes for
  * the 'open namespace' codefix/completion item
  * the way function-typed parameters are rendered
  * fantomas updates
  * fsharplint reenablement

### 5.5.5 - 15.05.2021

* Update FSAC to version 0.46.0 get fixes for
  * fsharp/signature endpoint
  * analyzer support
  * 2 new codefixes

### 5.5.4 - 30.04.2021

* Update FSAC to get improved semantic token highlighting


### 5.5.3 - 23.04.2021

* Update FSAC to get improved edge-case detection logic in several endpoints

### 5.5.2 - 18.04.2021

* Update FSAC to get improved logic in `textDocument/signatureDate`

### 5.5.1 - 18.04.2021

* Update FSAC to fix a regression in `textDocument/completions`

### 5.5.0 - 17.04.2021

* Update FSAC:
  * Update Unused Binding CodeFix to handle more cases
  * Enable faster typechecking when signature files are present for a module
    * Happens transparently, but is mutually exclusive with analyzers.
  * Fix the display of units of measure in tooltips (float<m/s> instead of float<MeasureInverse<MeasureProduct<.....>>>)
  * Much better experience for signature help for function applications and method calls
  * Update the Generate Abstract Class CodeFix to work for abstract classes that aren't defined in F#

### 5.4.1 - 16.03.2021

* Update FSAC:
  * performance updates for project cracking

New options

* `FSharp.enableMSBuildProjectGraph`
  * Enables experimental support for loading workspaces with MsBuild's ProjectGraph. This can improve load times. Requires restart.


### 5.4.0 - 03.03.2021

* Update FSAC:
  * FCS 39
  * Improved project cracking for dotnet SDK 5.0.200 and up

### 5.3.2 - 06.02.2020

* Add publishing support for Open VSIX marketplace

### 5.3.0 - 03.02.2020

* Update FSAC:
  * LSP support for semantic highlighting
  * improvements to workspace/symbol

### 5.2.0 - 13.01.2020
* Update Fable to 3.X
* Only send FSharpDocumentation request when panel is open
* Don't start debug if build has failed
* Solution explorer: Compile order and better support for directories
* Update FSAC:
  * Improve semantic highlighting
  * Re-enable `.binlog` generation
  * Add additional path normalization to handle some edge cases in Windows
  * Improve `.fsproj` mainpulation commands
  * Update `ProjInfo` to `0.46`
  * Add server requests in LSP implementation

### 5.1.0 - 28.12.2020
* Handle potential exception in HighlightingProvider activation (GitPod)
* Update FSAC:
  * Improve memory usage in Background Service
  * Improve memory usage in main process by removing AST from internal cache
  * Update Fantomas to 4.4 prerelease
  * Don't parse `.fs` files that don't belong to `.fsproj`
  * Enable initial support for directly opened `.fsx` files

### 5.0.3 - 23.12.2020
* Update FSAC:
  * Update FCS to 38.0.2
    * fixes #r issues in scripts

### 5.0.1 - 22.12.2020
* Update FSAC:
  * Update DPI to 0.45.1

### 5.0.0 - 21.12.2020

* Update Fable to 2.X
* Update FSAC:
  * Update to .NET 5 runtime
  * Update to FCS 38
  * Implement fsproj watcher
  * Implement fsproj editing
  * Add range selection provider
  * Add many new code fixes
  * Default to .NET SDK for script typechecking
  * Port to new version of dotnet-proj-info
  * Update Fantomas to 4.3
  * Background service will now remove symbol cache entries from non-existing files
  * Improvements to pipeline hints
* Remove choice of FSAC runtime
* Remove option to pick MsBuild host
* Remove watcher on fsproj files
* Remove some old commands that have better alternatives nowadays
* Remove some not needed code, and general cleanup
* Remove Forge
* Use `dotnet fsi` as default
* Use background service as default
* Use reference code lenses as default
* Improve solution explorer:
  * Add support for C# projects
  * Display referenced NuGet packages
  * Fix bugs with displayed project references

### 4.17.0 - 10.09.2020
* Update FSAC:
  - update dotnet-proj-info
  - update fantomas to v4
  - implement pipeline hints
  - add fsx files to `normalizePath`
* Implement pipeline hints

### 4.16.0 - 10.08.2020
* Update FSAC:
  - update fantomas to v4 beta 2
  - update to FCS 37 (string interpolation supported, see [this thread](https://github.com/dotnet/fsharp/pull/8907#issuecomment-670229678) for instructions on how to try it out)
  - fix a bug in FSX script reference resolution

### 4.15.0 - 4.08.2020
* Update FSAC:
  - update fantomas to v4 beta 1
  - go-to-definition for external types
  - linter suggestion code actions are fixed
  - initial literate programming integration via FSharp.Formatting

### 4.14.0 - 17.06.2020
* Update FSAC:
  - add go-to (F12) for `#load`
  - improvements in autocomplete

### 4.13.0 - 16.06.2020
* Update FSAC:
  - Update FSharpLint rule url
  - Support sourcelink for F# dependencies

### 4.12.1 - 11.06.2020
* Update FSAC:
  - Fixes to `FSharpProject` command

### 4.12.0 - 09.06.2020
* Update -o prompt message with dotnet ne
* Fix error propagation for failed standalone project requests
* Update FSAC:
  - Fix signature help off by one
  - Fix path issue in windows (caching content for fsx files)
  - Preserve order of params, exceptions und type args in tool tips

### 4.11.1 - 06.05.2020
* Handle notifications about `.csproj` correctly
* Add fsi setting for .NET SDK `FSharp.fsiSdkFilePath`

### 4.11.0 - 06.05.2020
* Add GenerateBinlog config
* Update FSAC:
  - Try improve project loading to use internal DPI cache
  - Add GenerateBinlog config
  - Update to latest Fantomas
  - Update to latest `dotnet-proj-info` - huge performance improvements

### 4.10.1 - 03.05.2020
* Fix type of `FSharp.addFsiWatcher` setting
* Update FSAC:
  - Performance fix for the projects with more than 2 p2p references

### 4.10.0 - 30.04.2020
* Update FSAC:
  - Add integration with the extendable `#r`
  - Add support for `#r nuget`
  - Update to FCS 35.0
  - Update `dotnet-project-info`, Fantomas (4.0 preview), and FSharpLint
  - Add caching for `FSharpProjectOptions` for script files
  - Fix floats being reported with random generic parameter
  - Use `system.runtime` members instead of `environment` for determining platform

### 4.9.0 - 09.04.2020
* Add semantic highlighting
* Update FSAC:
  - add support for semantic highlighting
  - update Fantomas to 3.3.0

### 4.8.1 - 25.03.2020
* Update FSAC:
  - support loading project with overridden `BaseIntermediateOutputPath`
* Recognize .gitignore paths that contain slashes at the start or the end.

### 4.8.0 - 23.03.2020
* Update to latest version of LSP client
* Update FSAC:
  - fix default SDK root path for Linux
* Add notification if `.gitignore` doesn't contain `.ionide` and `.fake`
* Add notification if FSAC is run on .Net Core but `useSdkScripts` is not enabled
* Add FSI Watcher

### 4.7.0 - 09.03.2020
* Use new documentation parser
* Generate documentation using xmldocs rather than
* Update FSAC:
  - new documentation parser

### 4.6.4 - 09.03.2020
* Update FsGrammar definition
* Update FSAC:
  - Update FCS to 34.1.1
  - Update FSharp.Analyzers.SDK to 0.4
  - Update FAKE integration

### 4.6.3 - 28.02.2020
* Update FSAC:
  - Update FSharp.Analyzers.SDK (FCS 34.1)

### 4.6.2 - 26.02.2020
* Update FSAC:
  - Use prerelease version of Fantomas (FCS 34.1)

### 4.6.1 - 26.02.2020
* Update FsGrammar definition
* Update FSAC:
  - Update FCS to 34.0
  - Update dotnet-proj-info to 0.38
  - Update FsLint to 0.13.3


### 4.6.0 - 19.02.2020
* Infrastructure - update build process and dependencies
* Update Forge - add `rollForward`
* Update FSAC:
  - Update FSharp.Analyzers.SDK to 0.3.0
  - Update FSI references version-finding algorithm to probe packs dir as well as runtimes dir
  - Allows analyzer paths to be absolute
  - Fix return type in signatures in documentation formatter
  - Introduce new logger
  - Update Fantomas to 3.2.0
  - Lot of small bug fixes and code improvements (thanks @forki)

### 4.5.0 - 18.01.2020
* Solution explorer - Add .fs file extension if needed
* FSAC  `dotnet new` integration, replace Forge scaffolding
* Update FSAC:
  - Fix issues with importing files in scripts by relative path
  - `dotnet new` integration

### 4.4.5 - 15.01.2020
* Add support for untitled files
* Update FSAC:
  - Add support for untitled files

### 4.4.4 - 15.01.2020
* Update FSAC:
  - Normalize path before serching in state in background service

### 4.4.3 - 09.01.2020
* Update FSAC:
  - Fix off-by-one on doc formatting
  - Using Fantomas 3.2 beta and reading the config file.

### 4.4.2 - 24.12.2019
* Update FSAC:
  - Update FSharp.Analyzers.SDK to 0.2

### 4.4.1 - 18.12.2019
* Update FSAC:
  - Fix a bug regarding `.fsx` files with new ProjectSystem (internal)

### 4.4.0 - 17.12.2019
* Update FSAC:
  - Update to FCS 33
  - Update to Fantomas 3.1
  - Fix `<Note>` display in autocomplete
  - Pick .Net Core TFM for scripts based on the runtime we detect
  - Support `--load` and `--use` directives for F# scripts
  - Reimplement F# Analyzers support
  - Refactor ProjectSystem (internal)

### 4.3.2 - 25.11.2019
* Update FSAC:
  - Fix how we assign FSI options
  - Fix struct tuple rendering

### 4.3.1 - 11.11.2019
* Update FSAC:
  - Fix `array` formatting with tuples
  - More fixes to `.fsx` files

### 4.3.0 - 10.11.2019
* FSI - no longer chunk messages because underlying node lib is better
* Set/pick up configured dotnet root
* Fix logic around dotnet detection on Ionide startup
* fix FSI spawning logic when the user is .net sdk but lacks .net framework
* Update FSAC:
  - Fix issue with `[]` formatting in signatures
  - update `.fsx` references logic
  - remove old stdio protocol

### 4.2.0 - 08.10.2019
* Update FSAC:
  - fix problem with resolving dependencies for `.fsx` files
  - add experimental support for the formatting documents (Fantomas integration)
  - add experimental support for `dotnet fsi` (`FSharp.useSdkScripts` setting)
  - fix double ticked display names in tooltips problems

### 4.1.0 - 17.09.2019
* Add `packages` folder to ignored directories in project search
* show project items
* update FSAC:
  - remove redundant text from lint warning and assign them a code
  - Show opens suggestions beofre use fully qualified names
  - handle null initializationOptions

### 4.0.6 - 12.07.2019
* Add gitignore component (disabled for now)
* Update FSAC:
  - Fix the URI problem on Windows

### 4.0.5 - 11.07.2019
* Update grammar definition
* Workaround SQLite native dependency on .Net Framework runtime.

### 4.0.4 - 05.07.2019
* Update grammar definition
* Update FSAC:
  - Fix the error duplication workaround

### 4.0.1 - 04.07.2019
* Update FSAC:
  - FCS 30 - performance fixes for the spelling suggestions in error messages
  - enable spelling suggestions in error messages
  - fix Background Service on the `net` + Windows.
  - improvements in FAKE support
* Add `FSharp.fsac.netExePath` to enabled debugging with `net` version of FSAC

### 4.0.0 - 24.06.2019
* Use LSP as communication protocol with FSAC
* Use Dotnet.ProjInfo.Workspace as only way to parse project files
* Use .Net Core version of Forge
* Remove `workspaceMode` setting - we always use FSAC based search to detect projects or solutions in workspace
* Remove `workspaceLoader` setting - we always use FSAC workspace based project loading.
* Set `fsacRuntime` to `netcore` by default - recommended way of using Ionide 4.0 is running it on .Net Core. .Net SDK is only strict requirement.
* Remove `logLanguageServiceRequests` and `logLanguageServiceRequestsOutputWindowLevel` settings - due to the fact Ionide is really thin layer we now use LSP based logging (hidden setting: `"FSharp.trace.server":"verbose"`) and additional FSAC logging (`verboseLogging`)
* Remove `toolsDirPath` setting - it was never used anyway
* Set`externalAutocomplete` to `false` by default - first of all the feature was not working too well (often inserting the repeated namespaces), secondly it caused responsiveness problems (just due to the fact it was huge number of suggestions on even small projects - it turns out there are lot of entities in .Net Framework)
* Replace `enableBackgroundSymbolCache` setting with `enableBackgroundServices` and set it to `true` by default - there has been huge FSAC refactoring around Background Service that should positively impact Ionide responsiveness (potential cost is RAM usage)
* Set `enableReferenceCodeLens` to `true` by default - as `enableBackgroundServices` is `true` by default, we can enable additional Code Lenses as well.
* Remove `customTypecheckingDelay` setting
* Remove `disableInMemoryProjectReferences` setting
* `F#: New Project` command now creates F# Core projects
* Improve support for FAKE scripts
* Add FAKE Target outline for FAKE scripts

### 3.38.0 - 21.05.2019
* Fix document symbol not displaying childs of top module/namespace
* Add FSDN command

### 3.37.0 - 10.05.2019
* Add "Diable notification" to the project parsing failed in all cases
* Fix rename of symbols using qualified identifier
* Fix styling of parameter hints
* Add progress notification for MsBuild operations
* Add SelectionRange provier
* Update FSAC:
    - fix Go-to-Symbol-in-Workspace
    - SelectionRange backend

### 3.36.2 - 06.05.2019
* Replace "not in project warning" with status bar indicator
* Ensure "Not in project" warning show when it should
* Add Declared Types to the panel

### 3.36.1 - 06.05.2019
* Update FSAC
* Add missing config for FSharp.notifyFsNotInFsproj
* Show project loading error notifications only for fsproj
* Fix run command
* Add Info Panel feature

### 3.35.0 - 02.04.2019
* Update FSAC:
    - FCS 28.0
    - go-to-implementation backend
* Implement go-to-implementation feature
* Tweak the configuration matrix to stop bugging users about their life choices (FSAC runtime setting)
* Have the suggestions give action buttons that the user can click to set the fsacruntime values
* Prompt users to reload after changing runtime
* Remove `fsharp.MoveFileUp/Down` default keybindings
* Remove `outputChannels` from contribution points
* Updte DocumentSymbolProvider to DocumentSymbol API
* Add progress notification for background restore
* Add notification for the .fs file not in .fsproj
* Add disable notification button to Project parsing failed error message
* Allow users to set a configurable FSAC url


### 3.34.0 - 28.02.2019
* Update FSAC to 0.37.0:
    - performance improvements
    - support for Anonymous Records
* Update Dotnet.ProjInfo to 0.31 for improved project loading and detection
* Minor fixes (typos etc.)

### 3.33.0 - 07.02.2019
* Only display smart indent notification if affected
* Upgrade to VS Code's webview API and fix show project status
* Improve the error message if the error is about SDK version
* Add automatic restore retry for any failure that's not caused by `dotnet restore`
* Remove duplicate openProjectFile icon when project not restore
* Add icon to see the status of failed project
* Do not consider all NetCore projects as executable
* Switch the Get Help URL to the new website
* Automatic restore retry only for the SDK projects

### 3.32.0 - 31.01.2019
* Add interface stub generation
* Fix build script - FSharp.Compiler.Service.ProjectCrackerTool path
* Add Open Collective link to readme

### 3.31.0 - 27.01.2019
* Remove CodeOutline feature
* Enable LineLens by default when CodeLens is disabled
* Use fsiAnyCpu when getting FSI path from FSAC
* Enable auto reveal of solution explorer
* update .NET branding
* Add node_modules to default excludeProjectDirs
* Add smart indent support
* Update FSAC - updated FSharpLint, fix `.fsx` support on `netcore` runtime.


### 3.30.0 - 16.11.2018
* Fix file watchers that doesn't consider `FSharp.excludeProjectDirectories`
* Automatically show terminal used for debugging
* Add more info to the solution explorer
* Try to fix project loading race condition
* Save default solution to workspace configuration
* Nicer display for the solution picker
* Make sure projects are loaded on the plugin startup (additional fix for project loading race condition)
* Show project explorer on startup (`FSharp.showExplorerOnStartup`)
* Update grammar definition
* Add option to control TouchBar integration (`FSharp.enableTouchBar`)

### 3.29.0 - 20.10.2018
* Format output type in CodeLenses correctly
* Fix wrong namespace suggestion
* Add autocomplete for # directives
* Add custom delay for the type checking (`FSharp.customTypecheckingDelay`)
* Remove unnecessary logging
* package.json: small grammar improvements
* Reword Multiple Target Framework warning
* Handle autocomplete open insert correctly
* Fix CodeLens reference counter bug
* Fixes and workaround for the ProjectCracker - result of investigating FSAC behavior on visualfsharp/FSharp.sln
* Add support for disabling in-memory project references
* Add to tooltip type description if the symbol is constructor
* Support keywords in helptext command (keyword documentation in autocomplete)
* Update grammar definition

### 3.28.0 - 12.10.2018
* Improvements for the getting started UX (error messages about mono/.Net SDK etc)
* Fixed too aggressive parameter hints bug
* Autocomplete improvements - fixed autocomplete for literal values

### 3.27.0 - 14.09.2018
* EXPERIMENTAL. Add custom analyzers support

### 3.26.0 - 12.09.2018
* EXPERIMENTAL. Enable references code lenses with `FSharp.enableReferenceCodeLens`. Requires experimental `FSharp.enableBackgroundSymbolCache`
* Add `.fable` folder to excluded folders by default
* Fix parameter hints
* Fix MSDN query link

### 3.25.4 - 31.08.2018
* Update FSAC - VS 2017 breaking changes
* Update Grammar
* restore behavior of FSharp.fsiFilePath
* Improved the word pattern regex.
* Normalize leading space for docs tooltips
* Ionide code base refactoring to follow F# style guidelines

### 3.25.3 - 20.08.2018
* Update FSAC - VS 2017 breaking changes

### 3.25.1 - 01.08.2018
* Make tooltips nicer - make sure that XML docs are converted correctly to the markdown, various fixes around signatures formatting, minimize module signature
* Update Fable.HtmlConverter
* Fix release process for updated version of FSAC.

### 3.25.0 - 27.07.2018
* Add navigation to external definitions (decompiled C# code)

### 3.24.2 - 27.07.2018
* EXPERIMENTAL Enable background symbol cache with `FSharp.enableBackgroundSymbolCache`
* Split logging from symbol cache to its own output channel
* Handle `[Debug]` output from FSAC

### 3.24.1 - 27.07.2018
* Use hint instead of information for record stub action
* Add debuncing for record stub generation
* Make sure record stub is checked only for F# files
* Add setting for disabling failed project notifications `FSharp.disableFailedProjectNotifications`

### 3.24.0 - 26.07.2018
* Update FSAC - latest FCS, F# 4.5, performance fixes, background symbol caching
* Add notifications if project loading or parsing failed
* Improve tooltip display
* Implement RecordStubGenerator and fix UnionCaseGenerato
* Add ability to send text to FSI through command arguments

### 3.23.0 - 18.07.2018
* Use Unnecessary tag for unused declaration
* Use Unnecessary tag for unused opens
* Update dependencies - grammar fixes

### 3.22.4 - 18.07.2018
* Even more grammar fixes
* Fix launcher detection (.Net Core vs normal exe)

### 3.22.3 - 06.07.2018
* More grammar fixes

### 3.22.2 - 06.07.2018
* Update FSAC - better Paket integration

### 3.22.1 - 03.07.2018
* Update grammar

### 3.22.0 - 06.06.2018
* Fix process spawn bug
* Update gramar
* Update FSAC - make sure we don't start background project parsing if it's turned off

### 3.21.0 - 30.05.2018
* Update FSAC - latest FCS (nightly)
* Update grammar
* Update Fable dependencies to latest
* Add HTML to Elmish

### 3.20.8 - 25.05.2018
* Update deps - multiple F# grammar fixes and markdown highlighting for comments
* Update FSAC - fix helptext caching bug

### 3.20.6 - 24.05.2018
* Update deps - F# grammar fix

### 3.20.5 - 22.05.2018
* Improve SignatureData generation + auto comment in doc comment section
* Update FSAC - fix for abstract member and member override signatures

### 3.20.4 - 15.05.2018
* Use a different ID for views in the fsharp activity container
* Inline icons in solution explorer

### 3.20.3 - 07.05.2018
* Update FSAC - fixes to workspace load

### 3.20.0 - 07.05.2018
* Add "fast" build to the API
* Add an F# activity in the left bar
* Fix undefined error in QuickInfoProject
* Fix project explorer - node id
* Update FSAC - fixes to workspace load performance
* Make workspace load default project loader mode

### 3.19.4 - 21.04.2018
* Update FSAC - fix code lens signatures for getters.

### 3.19.3 - 20.04.2018
* Fix `F#: Get Help` command
* Fix `Go-to` command - don't navigate to files that don't exist
* Update FSAC - constrains in tooltips
* Update grammar - small fixes

### 3.19.2 - 11.04.2018
* Use VSCode native icontheme support
* Fix proxy problem
* Ensure QuickInfo is hidden on non F#

### 3.19.1 - 09.04.2018
* Only use log levels that exists

### 3.19.0 - 08.04.2018
* Reflect the log levels on the dev console
* Avoid infinite restore loops
* Put the ;; on the next line when sending code to FSI when there's a comment
* Fix code outline for fsx files starting with `namespace global`
* Fix `Get Log` command
* Fix `New Project` command - empty directory

### 3.18.2 - 29.03.2018
* If a restore failed don't enter an infinite loop
* Tweak silentcd & friends in FSI

### 3.18.1 - 29.03.2018
* Fix forge path quotting.
* Remove notify printfn
* Update FSAC - latest FCS - lower memory usage

### 3.18.0 - 26.03.2018
* Remove Expecto integration
* Fix Forge integration with whitespaces in path
* Update FSAC - latest FCS - lower memory usage

### 3.17.3 - 08.03.2018
* Update FSAC - latest FCS
* Support for F# SDK 10.1

### 3.17.2 - 22.02.2018
* Update Forge - should fix Forge startup problem
* Fix startup behaviour

### 3.17.1 - 22.02.2018
* Update to latest FCS

### 3.17.0 - 21.02.2018
* Add JSON schema for WebSharper config file

### 3.16.0 - 18.02.2018
* Update LineLenses settings
* Use rename when replacing PascalCase literals
* Some fixes to CodeOutline UX
* Add diagnostic commands

### 3.15.8 - 11.01.2018
* Update Forge
* Fix run Expecto tests unquoted project file path

### 3.15.7 - 22.12.2017
* Update FSAC - try to fix ValueTupple.dll error
* Set `cwd` for build in debugger settings

### 3.15.6 - 22.12.2017
* Update FSAC - update F# Core to latest

### 3.15.5 - 22.12.2017
* Add project info on status bar

### 3.15.4 - 22.12.2017
* Fix rename QuickFix with backtick members

### 3.15.3 - 22.12.2017
* Fix resolving namespaces

### 3.15.2 - 22.12.2017
* Better sorting of autocomplete suggestions
* Better autocomplete for keywords
* Don't provide autocomplete in comments, strings, inside operators, inside keywords etc.
* Don't add `Attribute` suffix for completions inside `[< ... >]`

### 3.15.1 - 21.12.2017
* Fix .Net Core FSAC version

### 3.15.0 - 21.12.2017
* Update FSAC - latest nightly FCS
* Don't simplify open statements
* Don't suggest rename to `_` for functions
* Don't suggest opening namespace that is already open
* Add generic parameter info to tooltips
* Add static members to external symbols autocomplete

### 3.14.5 - 15.12.2017
* Sync auto restoring of projects

### 3.14.3 - 6.12.2017
* Fix quick info panel

### 3.14.2 - 29.11.2017
* Fix project loading bug

### 3.14.0 - 27.11.2017
* Add automatic project reloading and restoring. Makes sure cache is invalidated after `paket install` was run. Solves multiple issues requiring reload of the window such as adding new project, adding file to project, adding package to project with Paket etc.

### 3.13.2 - 23.11.2017
* Send `projectsInBackground` command only for f# files
* Fix Forge project creation if choosen directory name is blank
* Fix project properties page using workspace notification
* Update FSAC - project cracking fix and varius small fixes

### 3.13.0 - 21.11.2017
* Change background type checking strategy... again
* Add configuration for external symbols autocomplete `FSharp.externalAutocomplete`
* EXPERIMENTAL - Add .Net Core version of FsAutoComplete, enabled with `FSharp.fsacRuntime`

### 3.12.5 - 17.11.2017
* Update FSAC - small performance fixes and one critical crash error handling

### 3.12.4 - 16.11.2017
* Handle object name in members for unused declarations

### 3.12.3 - 16.11.2017
* Update FSAC - Fix position of inserted open declarations

### 3.12.2 - 12.11.2017
* Add MsBuild commands for solution to command palette

### 3.12.1 - 08.11.2017
* Fix inserting open statements in autocomplete, make sure it's insert instead of replace.

### 3.12.0 - 07.11.2017
* Use non-blocking notifications to get solution wide errors
* Collapsing / expending for code outline
* Add autocomplete for external (unopen namespaces/modules) symbols

### 3.11.0 - 03.11.2017
* Add go-to-type-definition provider
* Small UI fixes in commands in project explorer

### 3.10.1 - 02.11.2017
* Update FSAC (nightly FCS)
* Fix simplify name opens analyzer - false positives on the values
* Fix unused value analyzer - Active Pattern cases

### 3.10.0 - 01.11.2017
* Remove legacy (readonly) FSI
* Add generate/send references for FSI to project explorer

### 3.9.0 - 29.10.2017
* Add nicer tooltips - formatted signature, more styling, assembly.

### 3.8.4 - 27.10.2017
* Update FSAC - Fix find all usages

### 3.8.3 - 26.10.2017
* Update Forge version

### 3.8.2 - 23.10.2017
* Delete the current line where `Unused Open`'s quick fix is applied
* Fix crashed caused by project file including file not existing on disk

### 3.8.1 - 20.10.2017
* Fix unused opens analyzer

### 3.8.0 - 17.10.2017
* Add unused opens analyzer
* Add unused declarations analyzer
* Add simplify name analyzer

### 3.7.0 - 17.10.2017
* Fix creating project notifications
* Add restore commands to project explorer
* Makes go-to-symbol in workspace faster

### 3.6.0 - 16.10.2017
* Remove automatic project modification
* Add `add file (above/below)` commands for project explorer

### 3.5.1 - 08.10.2017
* Fix autocomplete problem

### 3.5.0 - 08.10.2017
* QuickInfo UX fixes
* Fix autocomplete helptext and tooltips rendering
* Update FSAC - cancellation of requests
* Make file parsing delay depend on file size

### 3.4.0 - 05.10.2017
* Initial support for MacOS TouchBar icons

### 3.3.4 - 04.10.2017
* Update FSAC - faster parsing

### 3.3.3 - 01.10.2017
* Make CodeLenses (and finding declarations faster)

### 3.3.2 - 30.09.2017
* Show symbol in the center of the screen when using Code Outline navigation
* Fix Code Outline startup if no editors opened

### 3.3.1 - 30.09.2017
* Add Code Outline panel

### 3.2.1 - 27.09.2017
* Add `Add Project to solution` command

### 3.2.0 - 26.09.2017
* Add run and debug default project commands
* Enable `resolve namespace or module` by default
* Fix parsing signature files
* Linelens colors
* Update to latest FSAC

### 3.1.0 - 13.09.2017
* Add MsBuild commands for solution node in project explorer

### 3.0.0 - 13.09.2017
* Go-to for #load
* LineLenses (inlined CodeLenses replacement)
* `Sln` support
* Forge 2.0 support

### 2.34.4 - 28.08.2017
* Move CodeLenses to SignatureData - more robust formatting

### 2.34.3 - 27.08.2017
* Update FSAC - F# 4.1 in scripts

### 2.34.2 - 27.08.2017
* Fix .Net Core Expecto runner

### 2.34.1 - 27.08.2017
* Automatically clear old cache files

### 2.34.0 - 27.08.2017
* Add automatic detection of MsBuild host
* Add `Run` command to the Project explorer
* Add support for .Net Standard 2.0

### 2.33.2 - 18.08.2017
* Fix rename with apostrophe

### 2.33.1 - 17.08.2017
* Fix typo in command name.

### 2.33.0 - 16.08.2017
* Show the project view only when there are projects to see
* Move 'Add project' to navigation commands with an icon
* Add rename file to project explorer
* Order and group file context menu for project explorer
* Add move to folder command for project explorer
* Add refresh and clear cache commands to project explorer
* Add detection for MsBuild 15
* Add MsBuild commands to project explorer

### 2.32.2 - 14.08.2017
* Use icons from theme in project explorer

### 2.32.1 - 12.08.2017
* Log error details when the language service fail to start
* Add "Open project file" command to project explorer
* Don't show .dll extension for references in project explorer
* Remove the files level in project explorer

### 2.32.0 - 09.08.2017
* Add folders to project explorer

### 2.31.0 - 02.08.2017
* Hide most commands from Command Palette when not necessary
* Add remove/add project references commands to solution explorer

### 2.30.0 - 01.08.2017
* Remove `webpreview`
* Add commands for file ordering in solution explorer

### 2.29.1 - 01.08.2017
* Add folders to file paths in solution explorer

### 2.29.0 - 31.07.2017
* Add documentation generator - `F#: Generate Documentation` command

### 2.28.0 - 27.07.2017
* Change activation events - use workspace contains
* Add `F#: Clear Cache` command

### 2.27.15 - 19.07.2017
* Add Experimental caching for project cracking

### 2.27.14 - 15.07.2017
* Make `F# language service` logging enabled by default

### 2.27.13 - 14.07.2017
* Fix linter initialization

### 2.27.12 - 14.07.2017
* Update FSAC (fix C# project references)
* Enhance `F# language service` logging

### 2.27.11 - 13.07.2017
*  Show Expecto Watch mode on Status Bar only if Expecto is used in workspace

### 2.27.10 - 12.07.2017
* Fix Expecto `dotnet` CLI runner

### 2.27.9 - 12.07.2017
* Fix Expecto .Net SDK detection

### 2.27.8 - 04.07.2017
* Add support for the .Net Core / `dotnet` CLI MsBuild

### 2.27.7 - 02.07.2017
* Change CodeLens caching method

### 2.27.6 - 23.06.2017
* Check if linter is enabled before linting

### 2.27.5 - 19.06.2017
* Add support for multi targetting in new `fsproj`

### 2.27.4 - 19.06.2017
* Add error handling for expecto output parser

### 2.27.3 - 19.06.2017
* Linter runs after parse is completed
* Linter runs after linter quick fix

### 2.27.2 - 17.06.2017
* Add Fable (`fable-webpack`) problem matcher

### 2.27.0 - 16.06.2017
* Update FSAC - Experimental SDK 2.0 support
* Add progress bar for plugin startup
* Add `excludeProjectDirectories` setting
* Update code lenses only for active F# file
* Add icons for solution explorer

### 2.26.1 - 09.06.2017
* Initial implementation of Project Explorer

### 2.25.14 - 01.06.2017
* Default config for Tab Stops

### 2.25.13 - 22.05.2017
* Fix leak in Expecto Watch Mode

### 2.25.12 - 08.05.2017
* FSI detection on Windows should support F# 4.1 FSI

### 2.25.11 - 04.05.2017
* Update FSAC

### 2.25.10 - 23.04.2017
* Update FSAC
* Expecto integration supports .Net Core


### 2.25.9 - 13.04.2017
* Update FSAC

### 2.25.8 - 08.04.2017
* Fix QuickFix for compiler messages
* Done from train from Cambridge to London
* Special thanks to Phillip, Marcus, Riccardo and Jay

### 2.25.7 - 31.03.2017
* Update grammar
* Update FSAC - reverse to FCS11
* Update to latest FSAC - Fix `.fs` files without `.fsproj`

### 2.25.3 - 29.03.2017
* Update to latest FSAC - FCS12

### 2.25.2 - 16.03.2017
* Update FSAC
* Support for latest fsproj
* Try to fix "crashing tooltips and autocomplete" bug
* Format error notes for tooltip/healtext

### 2.24.1 - 16.03.2017
* Add more MsBuild config - keybord shortcut, and autoshow output panel

### 2.24.0 - 15.03.2017
* Fix MsBuild integration on *nix
* Handle `.fsi` files
* Update FSAC - update to latest FCS

### 2.23.9 - 05.03.2017
* Try to fix CodeLens cache... yet another time

### 2.23.8 - 04.03.2017
* Update FSAC - FCS `11.0.4`

### 2.23.7 - 02.03.2017
* Handle both cases in `getPluginPath`

### 2.23.6 - 02.03.2017
* Breaking change in getPluginPath

### 2.23.5 - 24.02.2017
* Fix bugs in Expecto integration

### 2.23.4 - 23.02.2017
* Update FSAC - FCS11
* Normalize paths for Forge... again
* Fix `missing command` CodeLens bug

### 2.23.3 - 23.02.2017
* Normalize paths for Forge

### 2.23.2 - 23.02.2017
* Enable F# breakpoints
* Add more handling for null responses.

### 2.23.1 - 21.02.2017
* Make `Resolve unopened namespaces and modules` code fix optional.
* Workaround for the character limit in terminal.

### 2.23.0 - 17.02.2017
* Improve CodeLens caching
* Update FSAC - FCS 10

### 2.22.4 - 16.02.2017
* Improve CodeLens performance (caching)
* Fix CodeLens and Linter startup behaviour

### 2.22.3 - 15.02.2017
* Update grammar

### 2.22.2 - 10.02.2017
* Update FSAC - new fsrpoj changes

### 2.22.1 - 05.02.2017
* Refresh CodeLens only after parse request
* Update grammar

### 2.22.0 - 02.02.2017
* Add union match patter case generator
* Try to fix proxy bug

### 2.21.1 - 26.01.2017
* New project - emit error message on common failures

### 2.21.0 - 26.01.2107
* Update FSAC version - declarations optimization

### 2.20.0 - 20.01.2017
* Initial MsBuild support

### 2.19.5 - 17.01.2017
* Grammar updates

### 2.19.4 - 15.01.2017
* Update FSAC - try to fix StackOverflow namespaces bug

### 2.19.3 - 15.01.2017
* Add an output channel for FSAC Stdout lines in DEBUG

### 2.19.2 - 14.01.2017
*  Consolidate failed and errored tests

### 2.19.1 - 10.01.2017
* Fix detecting git previews
* Fix UI notifications for Expecto (active file change)

### 2.19.0 - 09.01.2017
* Add Expecto watch mode
* Add UI Gutter notifications for Expecto tests
* Add all Expecto flags to the settings
* Add support for custom args passed to Expecto tests

### 2.18.2 - 05.01.2017
* Fix solution wide error reporting

### 2.18.1 - 04.01.2017
* Set cwd for Expecto tests to exe dir

### 2.18.0 - 03.01.2017
* Add support for go to MSDN help (VS' F1)

### 2.17.1 - 01.01.2017
* Fix startup behaviour (fix bug in parsing all projects, and handle linter)

### 2.17.0 - 31.12.2016
* Add support for FSharpLint fixes

### 2.16.1 - 30.12.2016
* Add some logging for Expecto

### 2.16.0 - 29.12.2016
* Experimental Expecto support.

### 2.15.5 - 28.12.2016
* Add upercase DU quick fix

### 2.15.4 - 27.12.2016
* Don't parse git previw buffors

### 2.15.3 - 23.12.2016
* Add unused value suggestion

### 2.15.2 - 23.12.2016
* Add new keyword suggestion

### 2.15.1 - 21.12.2016
* Fix ResolveNamespaces trigger

### 2.15.0 - 21.12.2016
* Add `Resolve unopened namespaces and modules`

### 2.14.0 - 19.12.2016
* Add `F#: New Project (ProjectScaffold)` command

### 2.13.2 - 14.12.2016
* Update FSAC - performance improvements for findings usages of file-local/project-internal symbols.

### 2.13.1 - 09.12.2016
* Update Forge

### 2.13.0 - 09.12.2016
* Add support for untitled files

### 2.12.2 - 08.12.2016
* Update syntax highlighting

### 2.12.0 - 30.11.2016
* Implement quick fix for types and record fields suggestions.

### 2.11.3 - 29.11.2016
* Update Forge version

### 2.11.1 - 26.11.2016
* Update Forge version
* Increase pause before `parse` request to 1000ms

### 2.11.0 - 23.11.2016
* Add support for .Net Core preview3

### 2.10.1 - 20.11.2016
* Optional save current file on `FSI: Send Last Selection`

### 2.10.0 - 18.11.2016
* Add `FSI: Send Last Selection` command
* Fix keywords completion


### 2.9.4 - 14.11.2016
* Parse only depending projects on file save

### 2.9.3 - 11.11.2016
* Use 127.0.0.1 instead of localhost (trying to fix offline-windows bug)

### 2.9.2 - 10.11.2016
* Update Forge

### 2.9.0 - 06.11.2016
* Move to background checking of project files (faster startup)
* Fix FSI start

### 2.8.10 - 02.11.2016
* Remove CodeLens escaping

### 2.8.9 - 30.10.2016
* Fix CodeLens cache

### 2.8.8 - 28.10.2016
* Add tooltips for keywords, add code lens for constructors
* Add parse errors
* Fix CodeLens for static members

### 2.8.6 - 27.10.2016
* Add CodeLens cache

### 2.8.5 - 27.10.2016
* Preserve focus in current window, so it doesn't jump to terminal

### 2.8.4 - 27.10.2016
* Small fix to linter integration
* Show terminal when sending text
* Show code lens for properties

### 2.8.2 - 24.10.2016
* use "signature" FSAC endpoint for Code Lens signatures
* fix finding all usages

### 2.8.1 - 21.10.2016
* Fix error highlighting on current buffer

### 2.8.0 - 20.10.2016
* Add `FSI: Generate script file with references from project` command
* Performance updates - cancelling requests
* Small tooltips improvements

### 2.7.3 - 20.10.2016
* Fix automatic project modification bug

### 2.7.2 - 20.10.2016
* Nice tooltips formatting
* Small changes to autocomplete message format

### 2.7.1 - 19.10.2016
* Use plain text for tooltips body

### 2.7.0 - 19.10.2016
* Add `FSI:  Send References from project` command

### 2.6.14 - 17.10.2016
* Fix regressions caused by refactoring in `2.6.12`

### 2.6.13 - 17.10.2016
* FSI - Handle terminal close event

### 2.6.12 - 16.10.2016
* Add additional error handling for New Project commands

### 2.6.11 - 14.10.2016
* Should CodeLens on let value bindings
* Don't show CodeLens on abstract members

### 2.6.9 - 11.10.2016
* Fix FSI start parameters regression
* Small fix to tooltips format

### 2.6.8 - 11.10.2016
* Fix FSI Startup behaviour

### 2.6.7 - 09.10.2016
* Fix inconsistent whitespaces in CodeLens
* Fix interface members in CodeLens

### 2.6.6 - 08.10.2016
* Update FSAC
* Fix tooltips, quickinfo and CodeLens for doubleticked names

### 2.6.5 - 07.10.2016
* Add description to F# New project
* Load list of templates from templates.json

### 2.6.4 - 06.10.2016
* Fix requires in CodeLens

### 2.6.3 - 05.10.2016
* CodeLens signatures showing only types

### 2.6.2 - 05.10.2016
* Small fixes to tooltips
* Legacy FSI can be enabled in options.

### 2.6.1 - 04.10.2016
* Add CodeLens showing type signature

### 2.5.5 - 30.09.2016
* Define additional FSI flags in settings

### 2.5.4 - 28.09.2016
* Add new line before ;; in FSI

### 2.5.3 - 27.09.2016
* Don't make lint request if Linter us turned off
* Add information that warnign comes from linter

### 2.5.2 - 26.09.2016
* Fix FSI startup

### 2.5.1 - 26.09.2016
* Fix FSI (Set `__SOURCE_DIRECTORY__`  and `__SOURCE_FILE__` correctly)

### 2.5.0 - 25.09.2016
* Move FSI to Terminal API

### 2.4.3 - 18.09.2016
* Add Nancy tempalte to list

### 2.4.2 - 23.08.2016
* Fix autocomplete bug

### 2.4.1 - 21.08.2016
* Add detailed logging to F# Language Service Output Channel

### 2.4.0 - 21.08.2016
* Add FSharpLinter integration

### 2.3.0 - 21.08.2016
* Add new logging system
* Fix keywords autocomplete edge case
* Add option to turn off keywords autocomplete

### 2.2.18 - 18.08.2016
* Fix path checking bug which resulted in supporting only F# 4.0

### 2.2.17 - 14.08.2016
* Updated FSAC internal dependencies
    - Upgrade to F# 4.0
    - FSharp.Compiler.Service 6.0.2
    - FSharp.Compiler.Service.ProjectCracker 6.0.2
    - Newtonsoft.Json 9.0.1
    - Suave 1.1.3
* Update Fable to 0.5.4

### 2.2.13 - 12.08.2016
* Fix startup behaviour
* Improve brace matching and completion

### 2.2.12 - 05.08.2016
* Don't create new FSI output panel for every `FSI: Start`

### 2.2.11 - 04.08.2016
* Update Forge version

### 2.2.9 - 31.07.2016
* Fix startup

### 2.2.8 - 28.07.2016
* Add New Project without FAKE command
* Handle cancellation for F# project commands.

### 2.2.7 - 23.07.2016
* Added FSAC request log (default off)

### 2.2.6 - 22.07.2016
* Add configuration option to specify FSI dir

### 2.2.5 - 22.07.2016
* Add new line before ;; when sending to FSI

### 2.2.4 - 21.07.2016
* Move cursor down only on send line to FSI
* Try to fix keyword autocomplete

### 2.2.3 - 21.07.2016
* Trigger autocomplete on .

### 2.2.2 - 21.07.2016
* Fix Forge new project issue.

### 2.2.1 - 21.07.2016
* Try to fix finding FSI Path on Linux / Mac

### 2.2.0 - 21.07.2016
* Don't add keywords to autocomplete unless it's simple tag (no keywords completion for Something. )
* Better handling of Send to FSI commands.
* Don't require FSI to be in PATH on Windwos anymore.

### 2.1.0 - 20.07.2016
* Add keywords to autocomplete

### 2.0.0 - 17.07.2016
* Rewritten in Fable
* Updated Forge version
* Updated FSAC version
* Automatic add/remove file from project is now optional (FSharp.automaticProjectModification)
* Performance improvements for solution-wide features (rename, finding references)
* Finding errors in all solution after saving file (visable in error panel)
* Navigate to symbol working for whole solution
* Forge templates refreshed on every plugin startup
* Changed plugin startup beahviour - priority on getting currently opened file parsed and get feature working, parsing projects for solution-wide features in the background
* Removed F# Formatting integration

### 1.9.2 - 18.06.2016
* Add and Remove reference

### 1.9.1 - 11.06.2016
* Remove Project reference

### 1.9.0 - 11.06.2016
* Add Project reference

### 1.8.4 - 10.06.2016
* Fix alt+enter keybinding

### 1.8.3 - 09.06.2016
* Update Forge integration

### 1.8.2 - 25.05.2016
* Update FSAC

### 1.8.0 - 23.05.2016
* Add Signature Helper support

### 1.7.0 - 19.05.2016
* Add Forge support

### 1.6.10 - 19.05.2016
* Update FSAC

### 1.6.8 - 28.04.2016
* Fix typo

### 1.6.7 - 28.04.2016
* Add better error message for FSAC spawn

### 1.6.6 - 27.04.2016
* Add better error message for FSI spawn

### 1.6.3 - 22.04.2016
* Make completion faster

### 1.6.2 - 21.04.2016
* Small performance fix

### 1.6.1 - 19.04.2016
* Add some more null checking

### 1.6.0 - 12.04.2016
* Add .Net Core support

### 1.5.2 - 18.03.2016
* Fix startup behaviour

### 1.5.0 - 16.03.2016
* Add rename
* Make finding references work in multiple projects

### 1.4.2 - 16.03.2016
* Revert languageId changes

### 1.4.1 - 14.03.2016
* Fix bracketmatching and commenting

### 1.4.0 - 14.03.2016
* Add current symbol highlighting
* Add XML comments for tooltips and autocomplete

#### 1.3.0 - 25.02.2016
* Add WebPreview
* Add FSharp.Formatting integration

#### 1.2.2 - 26.01.2016
* Add proper deactivation

#### 1.2.1 - 26.01.2016
* Update FSAC

#### 1.2.0 - 15.01.2016
* Send SilentCd and line number file when sending comands to Fsi

#### 1.1.2 - 14.01.2016
* Filter completion result on FSAC side

#### 1.1.1 - 05.01.2016
* Fix path error

#### 1.1.0 - 09.12.2015
* Update FSAC version

#### 1.0.7 - 08.12.2015
* Fix error handling in FSI

#### 1.0.6 - 02.12.2015
* Fix backtick autocomplete problem

#### 1.0.5 - 01.12.2015
* Fix quick info priority

#### 1.0.4 - 26.11.2015
* Fix grammar

#### 1.0.3 - 21.11.2015
* Fix dependency

#### 1.0.2 - 20.11.2015
* First release from FAKE

#### 1.0.1 - 19.11.2015
* Fix paths for Linux

#### 1.0.0 - 18.11.2015
* Public release

#### 0.1.0 - 10.10.2015
* We are live - yay!

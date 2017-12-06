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

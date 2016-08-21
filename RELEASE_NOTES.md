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

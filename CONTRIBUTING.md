# How to contribute

## Prerequisites

- [Visual Studio Code][vscode] 🙄
- [Mono][mono]
- [.Net Core 2.0][dotnet]
- [Node.js][nodejs]
- [Yarn][yarn]

## Building

Fork, from the github interface https://github.com/ionide/ionide-vscode-fsharp
 - if you don't use a certificate for commiting to github:
```bash
git clone https://github.com/YOUR_GITHUB_USER/ionide-vscode-fsharp.git
```
 - if you use a certificate for github authentication:
```bash
git clone git@github.com:YOUR_GITHUB_USER/ionide-vscode-fsharp.git
```

### First time build:
```bash
cd ionide-vscode-fsharp
yarn install
dotnet restore
cd src
dotnet restore
cd ..
./build.sh  # or build.cmd if your OS is Windows
```

Everything is done via `build.cmd` \ `build.sh`.

- `build Build` does a full-build, including package installation and copying some necessary files.<br/>
  It should always be done at least once after any clone/pull.
- If a git dependency fail to build paket won't re-do it you can run their build scripts manually:
  - In `paket-files\github.com\fsharp\FsAutoComplete` run `build LocalRelease`
  - In `paket-files\github.com\fsharp-editing\Forge` run `build Build`
- In VSCode two configurations are possible to run:
  - Use `Build and Launch Extension`
  - Start the `Watch` task and when a build is done start `Launch Only`

## Working with FSAC

1. Run `build.cmd Build` \ `build.sh Build`
1. Open Ionide-vscode-fsharp in VSCode.
2. Set `devMode` to `true` in `src/Core/LanguageService.fs`
3. Open FSAC in VS
4. Start FSAC.Suave in VS
5. Press F5 in VSCode to build Ionide and start experimental instance

[dotnet]: https://www.microsoft.com/net/download/core
[mono]: http://www.mono-project.com/download/
[nodejs]: https://nodejs.org/en/download/
[yarn]: https://yarnpkg.com/en/docs/install
[vscode]: https://code.visualstudio.com/Download

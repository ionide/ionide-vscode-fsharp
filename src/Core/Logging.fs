namespace Ionide.VSCode.FSharp

[<AutoOpen>]
module Logging =
    open Fable.Import.Node
    open Fable.Import.vscode
    open System

    type Level = DEBUG|INFO|WARN|ERROR
        with
            static member GetLevelNum = function DEBUG->10|INFO->20|WARN->30|ERROR->40
            override this.ToString() = match this with ERROR->"ERROR"|INFO->"INFO"|WARN->"WARN"|DEBUG->"DEBUG"
            member this.isGreaterOrEqualTo level = Level.GetLevelNum(this) >= Level.GetLevelNum(level)
            member this.isLessOrEqualTo level = Level.GetLevelNum(this) <= Level.GetLevelNum(level)

    let private writeDevToolsConsole (level: Level) (source: string option) (template: string) (args: obj[]) =
        // just replace %j (Util.format->JSON specifier --> console->OBJECT %O specifier)
        // the other % specifiers are basically the same
        let browserLogTemplate = String.Format("[{0}] {1}", source.ToString(), template.Replace("%j", "%O"))
        match args.Length with
        | 0 -> Fable.Import.Browser.console.log (browserLogTemplate)
        | 1 -> Fable.Import.Browser.console.log (browserLogTemplate, args.[0])
        | 2 -> Fable.Import.Browser.console.log (browserLogTemplate, args.[0], args.[1])
        | 3 -> Fable.Import.Browser.console.log (browserLogTemplate, args.[0], args.[1], args.[2])
        | 4 -> Fable.Import.Browser.console.log (browserLogTemplate, args.[0], args.[1], args.[2], args.[3])
        | _ -> Fable.Import.Browser.console.log (browserLogTemplate, args)

    let private writeOutputChannel (out: OutputChannel) level source template args =
        let formattedMessage = util.format(template, args)
        let formattedLogLine = String.Format("[{0:HH:mm:ss} {1,-5}] {2}", DateTime.Now, string level, formattedMessage)
        out.appendLine (formattedLogLine)

    let private writeBothIfConfigured (out: OutputChannel option)
              (chanMinLevel: Level)
              (consoleMinLevel: Level option)
              (level: Level)
              (source: string option)
              (template: string)
              (args: obj[]) =
        if consoleMinLevel.IsSome && level.isGreaterOrEqualTo(consoleMinLevel.Value) then
            writeDevToolsConsole level source template args

        if out.IsSome && level.isGreaterOrEqualTo(chanMinLevel) then
            writeOutputChannel out.Value level source template args

    /// The templates may use node util.format placeholders: %s, %d, %j, %%
    /// https://nodejs.org/api/util.html#util_util_format_format
    type ConsoleAndOutputChannelLogger(source: string option, chanMinLevel: Level, out:OutputChannel option, consoleMinLevel: Level option) =

        /// Logs a different message in either DEBUG (if enabled) or INFO (otherwise).
        /// The templates may use node util.format placeholders: %s, %d, %j, %%
        /// https://nodejs.org/api/util.html#util_util_format_format
        member this.DebugOrInfo
                        (debugTemplateAndArgs: string * obj[])
                        (infoTemplateAndArgs: string * obj[]) =
            // OutputChannel: when at DEBUG level, use the DEBUG template and args, otherwise INFO
            if out.IsSome then
                if chanMinLevel.isLessOrEqualTo(Level.DEBUG) then
                    writeOutputChannel out.Value DEBUG source (fst debugTemplateAndArgs) (snd debugTemplateAndArgs)
                elif chanMinLevel.isLessOrEqualTo(Level.INFO) then
                    writeOutputChannel out.Value INFO source (fst infoTemplateAndArgs) (snd infoTemplateAndArgs)

            // Console: when at DEBUG level, use the DEBUG template and args, otherwise INFO
            if consoleMinLevel.IsSome then
                if Level.DEBUG.isGreaterOrEqualTo(consoleMinLevel.Value) then
                    writeDevToolsConsole DEBUG source (fst debugTemplateAndArgs) (snd debugTemplateAndArgs)
                elif Level.INFO.isGreaterOrEqualTo(consoleMinLevel.Value) then
                    writeDevToolsConsole INFO source (fst infoTemplateAndArgs) (snd infoTemplateAndArgs)

        /// Logs a message that should/could be seen by developers when diagnosing problems.
        /// The templates may use node util.format placeholders: %s, %d, %j, %%
        /// https://nodejs.org/api/util.html#util_util_format_format
        member this.Debug (template, [<ParamArray>]args:obj[]) =
            writeBothIfConfigured out chanMinLevel consoleMinLevel DEBUG source template args
        /// Logs a message that should/could be seen by the user in the output channel.
        /// The templates may use node util.format placeholders: %s, %d, %j, %%
        /// https://nodejs.org/api/util.html#util_util_format_format
        member this.Info (template, [<ParamArray>]args:obj[]) =
            writeBothIfConfigured out chanMinLevel consoleMinLevel INFO source template args
        /// Logs a message that should/could be seen by the user in the output channel when a problem happens.
        /// The templates may use node util.format placeholders: %s, %d, %j, %%
        /// https://nodejs.org/api/util.html#util_util_format_format
        member this.Error (template, [<ParamArray>]args:obj[]) =
            writeBothIfConfigured out chanMinLevel consoleMinLevel ERROR source template args
        /// Logs a message that should/could be seen by the user in the output channel when a problem happens.
        /// The templates may use node util.format placeholders: %s, %d, %j, %%
        /// https://nodejs.org/api/util.html#util_util_format_format
        member this.Warn (template, [<ParamArray>]args:obj[]) =
            writeBothIfConfigured out chanMinLevel consoleMinLevel WARN source template args

namespace Ionide.VSCode.FSharp

[<AutoOpen>]
module Logging =
    open Fable.Import.Node
    open Fable.Import.vscode
    open System

    type Level = DBG|INF|WRN|ERR
        with
            static member GetLevelNum level = match level with DBG->10|INF->20|WRN->30|ERR->40        
            override this.ToString() = match this with ERR->"ERR"|INF->"INF"|WRN->"WRN"|DBG->"DBG" 
            member this.isGreaterOrEqualTo level = Level.GetLevelNum(this) >= Level.GetLevelNum(level)

    let internal write (out: OutputChannel option)
              (chanMinLevel: Level)
              (consoleMinLevel: Level option)
              (level: Level)
              (source: string option)
              (template: string)
              (args: obj[]) =
        if consoleMinLevel.IsSome && level.isGreaterOrEqualTo(consoleMinLevel.Value) then
            // just replace %j (Util.format->JSON specifier --> console->OBJECT %O specifier)
            // the other % specifiers are basically the same
            let browserLogTemplate = "[" + source.ToString() + "] " + template.Replace("%j", "%O")
            match args.Length with
            | 0 -> Fable.Import.Browser.console.log (browserLogTemplate)
            | 1 -> Fable.Import.Browser.console.log (browserLogTemplate, args.[0])
            | 2 -> Fable.Import.Browser.console.log (browserLogTemplate, args.[0], args.[1])
            | 3 -> Fable.Import.Browser.console.log (browserLogTemplate, args.[0], args.[1], args.[2])
            | 4 -> Fable.Import.Browser.console.log (browserLogTemplate, args.[0], args.[1], args.[2], args.[3])
            | _ -> Fable.Import.Browser.console.log (browserLogTemplate, args)

        if level.isGreaterOrEqualTo(chanMinLevel) then
            match out with
            | Some chan -> 
                let formattedMessage = util.format(template, args).Replace("\n", " ")
                let formattedLogLine = String.Format("[{0:HH:mm:ss} {1}] {2}", DateTime.Now, string level, formattedMessage)
                chan.appendLine (formattedLogLine)
            | _ -> ()

    /// The templates may use node util.format placeholders: %s, %d, %j, %%
    /// https://nodejs.org/api/util.html#util_util_format_format
    type ConsoleAndOutputChannelLogger(source: string option, chanMinLevel: Level, out:OutputChannel option, logToConsole: bool) =
        let consoleMinLevel = if logToConsole then Some DBG else Some WRN // always dump warnings and errors to console
        /// Logs a message that should/could be seen by developers when diagnosing problems.
        /// The templates may use node util.format placeholders: %s, %d, %j, %%
        /// https://nodejs.org/api/util.html#util_util_format_format
        member this.Debug (template, [<ParamArray>]args:obj[]) =
            write out chanMinLevel consoleMinLevel DBG source template args
        /// Logs a message that should/could be seen by the user in the output channel.
        /// The templates may use node util.format placeholders: %s, %d, %j, %%
        /// https://nodejs.org/api/util.html#util_util_format_format
        member this.Info (template, [<ParamArray>]args:obj[]) =
            write out chanMinLevel consoleMinLevel INF source template args
        /// Logs a message that should/could be seen by the user in the output channel when a problem happens.
        /// The templates may use node util.format placeholders: %s, %d, %j, %%
        /// https://nodejs.org/api/util.html#util_util_format_format
        member this.Error (template, [<ParamArray>]args:obj[]) =
            write out chanMinLevel consoleMinLevel ERR source template args
        /// Logs a message that should/could be seen by the user in the output channel when a problem happens.
        /// The templates may use node util.format placeholders: %s, %d, %j, %%
        /// https://nodejs.org/api/util.html#util_util_format_format
        member this.Warn (template, [<ParamArray>]args:obj[]) =
            write out chanMinLevel consoleMinLevel WRN source template args

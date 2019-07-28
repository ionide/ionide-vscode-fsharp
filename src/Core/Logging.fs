namespace Ionide.VSCode.FSharp

[<AutoOpen>]
module Logging =
    open Fable.Core
    open Fable.Core.JsInterop
    open Fable.Import.Node
    open Fable.Import.vscode
    open Ionide.VSCode.FSharp.Node.Util
    open System

    type Level =
        | DEBUG
        | INFO
        | WARN
        | ERROR
        with
        static member GetLevelNum =
            function
            | DEBUG -> 10
            | INFO -> 20
            | WARN -> 30
            | ERROR -> 40
        override this.ToString() =
            match this with
            | ERROR -> "ERROR"
            | INFO -> "INFO"
            | WARN -> "WARN"
            | DEBUG -> "DEBUG"
        member this.isGreaterOrEqualTo level = Level.GetLevelNum(this) >= Level.GetLevelNum(level)
        member this.isLessOrEqualTo level = Level.GetLevelNum(this) <= Level.GetLevelNum(level)

    let mutable private ionideLogsMemory = []

    let getIonideLogs () = ionideLogsMemory |> String.concat "\n"

    [<Emit("console[$0] ? console[$0]($1...) : void 0")>]
    let private consoleLog (_level : string, [<ParamListAttribute>] _args : obj list) : unit = failwith "JS only"

    let getConsoleLogArgs (level : Level) (source : string option) (template : string) (args : obj[]) =
        // just replace %j (Util.format->JSON specifier --> console->OBJECT %O specifier)
        // the other % specifiers are basically the same
        let browserLogTemplate = String.Format("[{0}] {1}", source.ToString(), template.Replace("%j", "%O"))
        let nameOnConsoleObject =
            match level with
            | DEBUG -> "debug"
            | INFO -> "info"
            | WARN -> "warn"
            | ERROR -> "error"
        let argList = args |> List.ofArray
        nameOnConsoleObject, (List.append [ box browserLogTemplate ] argList)

    let inline private writeDevToolsConsole (level : Level) (source : string option) (template : string) (args : obj[]) =
        let nameOnConsoleObject, logArgs = getConsoleLogArgs level source template args
        consoleLog(nameOnConsoleObject, logArgs)

    let private writeOutputChannel (out : OutputChannel) level source template args =
        let formattedMessage = Util.format(template, args)
        let formattedLogLine = String.Format("[{0:HH:mm:ss} {1,-5}] {2}", DateTime.Now, string level, formattedMessage)
        out.appendLine (formattedLogLine)

    let private writeToFile level template args =
        let formattedMessage = Util.format(template, args)
        let formattedLogLine = String.Format("[{0:HH:mm:ss} {1,-5}] {2}\n", DateTime.Now, string level, formattedMessage)
        // Only store the 200 last logs
        if ionideLogsMemory.Length >= 200 then
            ionideLogsMemory <- ionideLogsMemory.Tail @ [formattedLogLine]
        else
            ionideLogsMemory <- ionideLogsMemory @ [formattedLogLine]

    let private writeOutputChannelIfConfigured (out : OutputChannel option)
                                               (chanMinLevel : Level)
                                               (level : Level)
                                               (source : string option)
                                               (template : string)
                                               (args : obj[]) =
        if out.IsSome && level.isGreaterOrEqualTo(chanMinLevel) then
            writeOutputChannel out.Value level source template args

        // Only write FSAC logs into the file
        if source = Some "IONIDE-FSAC" then
            try
                if string args.[0] <> "parse" then
                    writeToFile level template args
            with
                | _ -> () // Do nothing

    let inline private writeBothIfConfigured (out : OutputChannel option)
                                             (chanMinLevel : Level)
                                             (consoleMinLevel : Level option)
                                             (level : Level)
                                             (source : string option)
                                             (template : string)
                                             (args : obj[]) =
        if consoleMinLevel.IsSome && level.isGreaterOrEqualTo(consoleMinLevel.Value) then
            writeDevToolsConsole level source template args

        writeOutputChannelIfConfigured out chanMinLevel level source template args

    /// The templates may use node util.format placeholders: %s, %d, %j, %%
    /// https://nodejs.org/api/util.html#util_util_format_format
    type ConsoleAndOutputChannelLogger(source : string option,
                                       chanDefaultMinLevel : Level,
                                       out : OutputChannel option,
                                       consoleMinLevel : Level option) =
        member val ChanMinLevel = chanDefaultMinLevel with get, set

        /// Logs a different message in either DEBUG (if enabled) or INFO (otherwise).
        /// The templates may use node util.format placeholders: %s, %d, %j, %%
        /// https://nodejs.org/api/util.html#util_util_format_format
        member this.DebugOrInfo (debugTemplateAndArgs : string * obj[])
                                (infoTemplateAndArgs : string * obj[]) =
            // OutputChannel: when at DEBUG level, use the DEBUG template and args, otherwise INFO
            if out.IsSome then
                if this.ChanMinLevel.isLessOrEqualTo(Level.DEBUG) then
                    writeOutputChannel out.Value DEBUG source (fst debugTemplateAndArgs) (snd debugTemplateAndArgs)
                elif this.ChanMinLevel.isLessOrEqualTo(Level.INFO) then
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
        member this.Debug (template, [<ParamArray>] args : obj[]) =
            writeBothIfConfigured out this.ChanMinLevel consoleMinLevel DEBUG source template args
        /// Logs a message that should/could be seen by the user in the output channel.
        /// The templates may use node util.format placeholders: %s, %d, %j, %%
        /// https://nodejs.org/api/util.html#util_util_format_format
        member this.Info (template, [<ParamArray>] args : obj[]) =
            writeBothIfConfigured out this.ChanMinLevel consoleMinLevel INFO source template args
        /// Logs a message that should/could be seen by the user in the output channel when a problem happens.
        /// The templates may use node util.format placeholders: %s, %d, %j, %%
        /// https://nodejs.org/api/util.html#util_util_format_format
        member this.Error (template, [<ParamArray>] args : obj[]) =
            writeBothIfConfigured out this.ChanMinLevel consoleMinLevel ERROR source template args
        /// Logs a message that should/could be seen by the user in the output channel when a problem happens.
        /// The templates may use node util.format placeholders: %s, %d, %j, %%
        /// https://nodejs.org/api/util.html#util_util_format_format
        member this.Warn (template, [<ParamArray>] args : obj[]) =
            writeBothIfConfigured out this.ChanMinLevel consoleMinLevel WARN source template args
        /// Logs a message that should/could be seen by the user in the output channel if the promise fail.
        /// The templates may use node util.format placeholders: %s, %d, %j, %%
        /// https://nodejs.org/api/util.html#util_util_format_format
        member this.ErrorOnFailed text (p : Fable.Import.JS.Promise<_>) =
            p.catch(fun err -> this.Error(text + ": %O", err))
            |> ignore

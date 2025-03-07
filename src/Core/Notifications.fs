namespace Ionide.VSCode.FSharp

open Fable.Import
open Fable.Import.VSCode
open Fable.Import.VSCode.Vscode
open DTO

[<RequireQualifiedAccess>]
module Notifications =
    type DocumentParsedEvent =
        {
            uri: string
            version: float
            /// BEWARE: Live object, might have changed since the parsing
            document: TextDocument
        }

    let onDocumentParsedEmitter = vscode.EventEmitter.Create<DocumentParsedEvent>()
    let onDocumentParsed = onDocumentParsedEmitter.event

    let private tooltipRequestedEmitter = vscode.EventEmitter.Create<Position>()
    let tooltipRequested = tooltipRequestedEmitter.event

    let mutable notifyWorkspaceHandler
        : Option<Choice<ProjectResult, ProjectLoadingResult, (string * ErrorData), string> -> unit> =
        None

    let testDetectedEmitter = vscode.EventEmitter.Create<TestForFile>()
    let testDetected = testDetectedEmitter.event

    let nestedLanguagesDetectedEmitter =
        vscode.EventEmitter.Create<NestedLanguagesForFile>()

    let nestedLanguagesDetected: Event<NestedLanguagesForFile> =
        nestedLanguagesDetectedEmitter.event

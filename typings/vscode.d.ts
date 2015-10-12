/*---------------------------------------------------------
 * Copyright (C) Microsoft Corporation. All rights reserved.
 *--------------------------------------------------------*/

declare module vscode {

	/**
	 * The command callback is a function which represents
     * a commend
	 */
	 interface CommandCallback {

		/**
		 *
		 */
		<T>(...args:any[]) : T;
		<T>(...args:any[]) : Thenable<T>;
	}

	/**
	 * The ```commands``` module contains function to register and execute commands.
	 */
	 module commands {

		/**
		 * Registers a command that can be invoked via a keyboard shortcut,
		 * an menu item, an action, or directly.
		 *
		 * @param command - The unique identifier of this command
		 * @param callback - The command callback
		 * @param thisArgs - (optional) The this context used when invoking {{callback}}
		 * @return Disposable which unregisters this command on disposal
		 */
		 function registerCommand(command: string, callback: CommandCallback, thisArg?: any): Disposable;

		/**
		 * Register a text editor command that will make edits.
		 * It can be invoked via a keyboard shortcut, a menu item, an action, or directly.
		 *
		 * @param command - The unique identifier of this command
		 * @param callback - The command callback. The {{textEditor}} and {{edit}} passed in are available only for the duration of the callback.
		 * @param thisArgs - (optional) The `this` context used when invoking {{callback}}
		 * @return Disposable which unregisters this command on disposal
		 */
		 function registerTextEditorCommand(command: string, callback: (textEditor:TextEditor, edit:TextEditorEdit) => void, thisArg?: any): Disposable;

		/**
		 * Executes a command
		 *
		 * @param command - Identifier of the command to execute
		 * @param ...rest - Parameter passed to the command function
		 * @return
		 */
		 function executeCommand<T>(command: string, ...rest: any[]): Thenable<T>;
	}

	 interface TextEditorOptions {
		tabSize: number;
		insertSpaces: boolean;
	}

    /**
     * A text document is an immutable representaton of text. To edit a document, use a ```TextEditor```.
     */
	 class TextDocument {

		constructor(uri: Uri, lines: string[], eol: string, languageId: string, versionId: number, isDirty:boolean);

		/**
		 * Get the associated URI for this document. Most documents have the file:// scheme, indicating that they represent files on disk.
		 * However, some documents may have other schemes indicating that they are not available on disk.
		 */
		getUri(): Uri;

		/**
		 * @return The file system path of the file associated with this document. Shorthand
		 * notation for ```TextDocument.getUri().fsPath```
		 */
		getPath(): string;

		/**
		 * @return Is this document representing an untitled file.
		 */
		isUntitled(): boolean;

        /**
         * @return true iff there are changes to be saved on disk
         */
		isDirty(): boolean;

        /**
         * Saves the underlying file with contents of this document.
         */
		save(): Thenable<boolean>;

		/**
		 * @return The language identifier associated with this document.
		 */
		getLanguageId(): string;

		/**
		 * @return The version number of this document (it will strictly increase after each change).
		 */
		getVersionId(): number;

		/**
		 * @return Get the entire text in this document.
		 */
		getText(): string;

		/**
         * @param range a range of the document
		 * @return Get the text in a specific range in this document.
		 */
		getTextInRange(range: Range): string;

		/**
         * @param a line number
		 * @return Get the text on a specific line in this document.
		 */
		getTextOnLine(line:number): string;

		/**
		 * Ensure a range sticks to the text.
		 */
		validateRange(range:Range): Range;

		/**
		 * Ensure a position sticks to the text.
		 */
		validatePosition(position:Position): Position;

		/**
		 * @return Get the number of lines in this document.
		 */
		getLineCount(): number;

		/**
         * @param line a line number
		 * @return Get the maximum column for the specified line
		 */
		getLineMaxColumn(line:number): number;

		/**
         * @param position a position
		 * @return Get the word under a certain position. May return null if position is at whitespace, on empty line, etc.
		 */
		getWordRangeAtPosition(position:Position): Range;
	}

    /**
     * A position represents a line and character of a ```TextDocument```.
     *
     * This type is *immutable* which means after creation its state cannot
     * be modified anymore.
     */
	 class Position {

		line: number;

		character: number;

		constructor(line: number, character: number);

		isBefore(other: Position): boolean;

		isBeforeOrEqual(other: Position): boolean;
	}


    /**
     * A range represents two ```Positions``` of a ```TextDocument```. Often
     * ranges cover multiple lines.
     *
     * This type is *immutable* which means after creation its state cannot
     * be modified anymore.
     */
     class Range {

		start: Position;

		end: Position;

		constructor(start: Position, end: Position);
		constructor(startLine: number, startColumn: number, endLine:number, endColumn:number);

		contains(position: Position ): boolean;
		contains(range: Range): boolean;

		/**
		 * @return `true` iff `start` and `end` are equal
		 */
		isEmpty(): boolean;

		/**
		 * @return `true` iff start and end are on the same line
		 */
		isOneLine(): boolean;
	}

    /**
     * A selection is a range define by a user gesture in a ```TextEditor```.
     * Selection have an anchor position, where a selection starts, and an
     * active position, where a selection ends and where the cursor is.
     *
     * This type is *immutable* which means after creation its state cannot
     * be modified anymore.
     */
	 class Selection extends Range {

		anchor: Position;

		active: Position;

		constructor(anchor: Position, active: Position);
		constructor(anchorLine: number, anchorColumn: number, activeLine:number, activeColumn:number);

        /**
         * @return true iff ```active``` !== ```start```
         */
		isReversed(): boolean;
	}

     class TextEditor {

		constructor(document: TextDocument, selections: Selection[], options: TextEditorOptions);

		/**
		 * Get the document associated with this text editor. The document will be the same for the entire lifetime of this text editor.
		 */
		getTextDocument(): TextDocument;

		/**
		 * Get the primary selection on this text editor. In case the text editor has multiple selections, the first one will be returned.
		 */
		getSelection(): Selection;

		/**
		 * Set the selection on this text editor.
		 */
		setSelection(value: Position ): Thenable<any>;
		setSelection(value: Range ): Thenable<any>;
		setSelection(value: Selection): Thenable<any>;

		/**
		 * Get the selections in this text editor.
		 */
		getSelections(): Selection[];

		/**
		 * Set the selections in this text editor.
		 */
		setSelections(value: Selection[]): Thenable<TextEditor>;

		/**
		 * Get text editor options.
		 */
		getOptions(): TextEditorOptions;

		/**
		 * Change text editor options.
		 */
		setOptions(options: TextEditorOptions): Thenable<TextEditor>;

		/**
		 * Perform an edit on the document associated with this text editor.
		 * The passed in {{editBuilder}} is available only for the duration of the callback.
		 */
		edit(callback:(editBuilder:TextEditorEdit)=>void): Thenable<boolean>;
	}

	/**
	 * A complex edit that will be applied on a TextEditor.
	 * This holds a description of the edits and if the edits are valid (i.e. no overlapping regions, etc.) they can be applied on a Document associated with a TextEditor.
	 */
	 interface TextEditorEdit {
		/**
		 * Replace a certain text region with a new value.
		 */
		replace(location: Position , value: string): void;
		replace(location: Range , value: string): void;
		replace(location: Selection, value: string): void;

		/**
		 * Insert text at a location
		 */
		insert(location: Position, value: string): void;

		/**
		 * Delete a certain text region.
		 */
		delete(location: Selection): void;
		delete(location: Range ): void;
	}

	/**
	 * A universal resource identifier representing either a file on disk on
	 * or another resource, e.g untitled.
	 */
	 class Uri {

		constructor();
		static parse(path: string): Uri;
		static file(path: string): Uri;
		static create(path: string): Uri;

		/**
		 * scheme is the 'http' part of 'http://www.msft.com/some/path?query#fragment'.
		 * The part before the first colon.
		 */
		scheme: string;


		/**
		 * authority is the 'www.msft.com' part of 'http://www.msft.com/some/path?query#fragment'.
		 * The part between the first double slashes and the next slash.
		 */
		authority: string;


		/**
		 * path is the '/some/path' part of 'http://www.msft.com/some/path?query#fragment'.
		 */
		path: string;

		/**
		 * query is the 'query' part of 'http://www.msft.com/some/path?query#fragment'.
		 */
		query: string;

		/**
		 * fragment is the 'fragment' part of 'http://www.msft.com/some/path?query#fragment'.
		 */
		fragment: string;

		/**
		 * Retuns a string representing the corresponding file system path of this URI.
		 * Will handle UNC paths and normalize windows drive letters to lower-case. Also
		 * uses the platform specific path separator. Will *not* validate the path for
		 * invalid characters and semantics. Will *not* look at the scheme of this URI.
		 */
		fsPath: string;

		/**
		 * Returns a canonical representation of this URI. The representation and normalization
		 * of a URI depends on the scheme.
		 */
		toString(): string;

		toJSON(): any;
	}

	 interface CancellationToken {
		isCancellationRequested: boolean;
		onCancellationRequested: Event<any>;
	}

	 class CancellationTokenSource {

		token: CancellationToken;

		cancel(): void;

		dispose(): void;
	}

	interface DisposableLike {
		dispose: () => any
	}

	/**
	 * Represents a type which can release resources, such
	 * as event listening or a timer.
	 */
	 class Disposable {

		/**
		 * Combine many disposables into one.
		 *
		 * @return Returns a new disposable which, upon dispose, will
		 * dispose all provided disposables
		 */
		static of(...disposables: Disposable[]): Disposable;

		/**
		 * Combine many disposable-likes into one. Use this method
		 * when having objects with a dispose function which are not
		 * instances of Disposable.
		 *
		 * @return Returns a new disposable which, upon dispose, will
		 * dispose all provides disposable-likes.
		 */
		static from(...disposableLikes: DisposableLike[]): Disposable;

		/**
		 * Creates a new Disposable calling the provided function
		 * on dispose
		 * @param callOnDispose Function that disposes something
		 */
		constructor(callOnDispose: Function);

		/**
		 * Dispose this object.
		 */
		dispose(): any;
	}

	/**
	 * Represents a typed event.
	 */
	 interface Event<T> {

		/**
		 *
		 * @param listener The listener function will be call when the event happens.
		 * @param thisArgs The 'this' which will be used when calling the event listener.
		 * @param disposables An array to which a {{IDisposable}} will be added. The
		 * @return
		 */
		(listener: (e: T) => any, thisArgs?: any, disposables?: Disposable[]): Disposable;
	}

	/**
	 * A file system watcher notifies about changes to files and folders
	 * on disk. To get an instanceof of a {{FileSystemWatcher}} use
	 * {{workspace.createFileSystemWatcher}}.
	 */
	 interface FileSystemWatcher extends Disposable {

		/**
		 * Happens on file/folder creation.
		 */
		onDidCreate: Event<Uri>;

		/**
		 * Happens on file/folder change.
		 */
		onDidChange: Event<Uri>;

		/**
		 * Happens on file/folder deletion.
		 */
		onDidDelete: Event<Uri>;
	}

	/**
	 *
	 */
	 interface QuickPickOptions {
		/**
		* an optional flag to include the description when filtering the picks
		*/
		matchOnDescription?: boolean;

		/**
		* an optional string to show as place holder in the input box to guide the user what she picks on
		*/
		placeHolder?: string;
	}

	/**
	 *
	 */
	 interface QuickPickItem {
		label: string;
		description: string;
	}

	/**
	 *
	 */
	 interface InputBoxOptions {
		/**
		* The text to display underneath the input box.
		*/
		prompt?: string;

		/**
		* an optional string to show as place holder in the input box to guide the user what to type
		*/
		placeHolder?: string;
	}

	/**
	 *
	 */
	 interface LanguageFilter {
		language?: string;
		scheme?: string;
		pattern?: string;
	}

	/**
	 *
	 */
	// type LanguageSelector = string|LanguageFilter|(string|LanguageFilter)[];

	/**
	 *
	 */
	 interface ReadOnlyMemento {

		/**
		 * @param key The name of a property to read.
		 * @param defaultValue The default value in case the denoted property doesn't exists.
		 * @return
		 */
		getValue<T>(key: string, defaultValue?: T): Thenable<T>;

		/**
		 *
		 */
		getValues<T>(defaultValue?: T): Thenable<T>;
	}

	/**
	 *
	 */
	 interface Memento extends ReadOnlyMemento {
		setValue(key: string, value: any): Thenable<void>;
	}

	/**
	 * Represents the severity of diagnostics.
	 */
	 enum DiagnosticSeverity {
		Warning = 1,
		Error = 2
	}

	/**
	 * Represents a location inside a resource, such as a line
	 * inside a text file.
	 */
	 class Location {
		constructor(uri: Uri, range: Position);
		constructor(uri: Uri, range: Range);
		uri: Uri;
		range: Range;
	}

	/**
	 * Represents a diagnostic, such as a compiler error or warning, along with the location
	 * in which they occurred.
	 */
	 class Diagnostic {

		constructor(severity: DiagnosticSeverity, location: Location, message: string, source?:string);

		severity: DiagnosticSeverity;

		location: Location;

		message: string;

		source: string;
	}

	// TODO@api, TODO@Joh,Ben
	// output channels need to be known upfront (contributes in package.json)
	 interface OutputChannel extends Disposable {
		append(value: string): void;
		appendLine(value: string): void;
		clear(): void;
		reveal(): void;
	}

	 interface ExecutionOptions {
		cwd?: string;
		env?: { [name: string]: any };
	}

	 interface TextEditorSelectionChangeEvent {
		textEditor: TextEditor;
		selections: Selection[];
	}

	 interface TextEditorOptionsChangeEvent {
		textEditor: TextEditor;
		options: TextEditorOptions;
	}

	 interface ITelemetryInfo {
		sessionId: string;
		machineId: string;
		instanceId: string;
	}

    /**
     * The ```window``` module contains functions to interact with the active window of VS Code.
     */
	 module window {

		 function getActiveTextEditor(): TextEditor;

		 var onDidChangeActiveTextEditor: Event<TextEditor>;

		 var onDidChangeTextEditorSelection: Event<TextEditorSelectionChangeEvent>;

		 var onDidChangeTextEditorOptions: Event<TextEditorOptionsChangeEvent>;

		 interface CommandInformationString {
			  title: string;
				command: string
		 }

		 interface CommandInformationCallback {
			  title: string;
				command: CommandCallback
		 }

		 function showInformationMessage(message: string, ...commands: CommandInformationString[]): Thenable<void>;
		 function showInformationMessage(message: string, ...commands: CommandInformationCallback[]): Thenable<void>;


		 function showWarningMessage(message: string, ...commands: CommandInformationString[]): Thenable<void>;
		 function showWarningMessage(message: string, ...commands: CommandInformationCallback[]): Thenable<void>;


		 function showErrorMessage(message: string, ...commands: CommandInformationString[]): Thenable<void>;
		 function showErrorMessage(message: string, ...commands: CommandInformationCallback[]): Thenable<void>;

		 function setStatusBarMessage(message: string, hideAfterSeconds?: number): Disposable;

		 function showQuickPick(items: string[], options?: QuickPickOptions): Thenable<string>;

		 function showQuickPick<T extends QuickPickItem>(items: T[], options?: QuickPickOptions): Thenable<T>;

		/**
		 * Opens an input box to ask the user for input.
		 */
		 function showInputBox(options?: InputBoxOptions): Thenable<string>;

		 function getOutputChannel(name: string): OutputChannel;

		/**
		 * ✂ - don't use. Will be cut soone!
		TODO@api move into a node_module
		 */
		 function runInTerminal(command: string, args: string[], options?: ExecutionOptions): Thenable<any>;
	}

	/**
	 * An event describing a change in the text of a model.
	 */
	 interface TextDocumentContentChangeEvent {
		/**
		 * The range that got replaced.
		 */
		range: Range;
		/**
		 * The length of the range that got replaced.
		 */
		rangeLength: number;
		/**
		 * The new text for the range.
		 */
		text: string;
	}

	 interface TextDocumentChangeEvent {
		document: TextDocument;
		contentChanges: TextDocumentContentChangeEvent[];
	}

	/**
     * The ```workspace``` module contains functions to interact with currently opened folder.
     */
	 module workspace {

		/**
		 * Creates a file system watcher. A glob pattern that filters the
		 * file events must be provided. Optionally, flags to ignore certain
		 * kind of events can be provided.
		 *
		 * @param globPattern - A glob pattern that is applied to the names of created, changed, and deleted files.
		 * @param ignoreCreateEvents - Ignore when files have been created.
		 * @param ignoreChangeEvents - Ignore when files have been changed.
		 * @param ignoreDeleteEvents - Ignore when files have been deleted.
		 */
		 function createFileSystemWatcher(globPattern: string, ignoreCreateEvents?: boolean, ignoreChangeEvents?: boolean, ignoreDeleteEvents?: boolean): FileSystemWatcher;

		// TODO@api - justify this being here
		 function getPath(): string;

		 function getRelativePath(path: string): string;
		 function getRelativePath(uri: Uri): string;

		// TODO@api - justify this being here
		 function findFiles(include: string, exclude: string, maxResults?:number): Thenable<Uri[]>;

		/**
		 * save all dirty files
		 */
		 function saveAll(includeUntitled?: boolean): Thenable<boolean>;

		 function getTextDocuments(): TextDocument[];
		 function getTextDocument(resource: Uri): TextDocument;
		 var onDidOpenTextDocument: Event<TextDocument>;
		 var onDidCloseTextDocument: Event<TextDocument>;
		 var onDidChangeTextDocument: Event<TextDocumentChangeEvent>;
		 var onDidSaveTextDocument: Event<TextDocument>;
	}

    /**
     * The ```languages``` module contains functions that are specific to implemnting a
     * language service.
     */
	 module languages {

		/**
		 * Add diagnostics, such as compiler errors or warnings. They will be represented as
		 * squiggles in text editors and in a list of diagnostics.
		 * To remove the diagnostics again, dispose the `Disposable` which is returned
		 * from this function call.
		 *
		 * @param diagnostics Array of diagnostics
		 * @return A disposable the removes the diagnostics again.
		 */
		 function addDiagnostics(diagnostics: Diagnostic[]): Disposable;


		 interface LanguageStatusMessage {
			octicon: string;
			message: string;
		}
		/**
		 *
		 */
		 function addInformationLanguageStatus(language: any, message: string , command: CommandCallback): Disposable;
		 function addInformationLanguageStatus(language: any, message: string , command: string): Disposable;
		 function addInformationLanguageStatus(language: any, message: LanguageStatusMessage, command: CommandCallback): Disposable;
		 function addInformationLanguageStatus(language: any, message: LanguageStatusMessage, command: string): Disposable;


		/**
		 *
		 */
		 function addWarningLanguageStatus(language: any, message: string , command: CommandCallback): Disposable;
 		 function addWarningLanguageStatus(language: any, message: string , command: string): Disposable;
 		 function addWarningLanguageStatus(language: any, message: LanguageStatusMessage, command: CommandCallback): Disposable;
 		 function addWarningLanguageStatus(language: any, message: LanguageStatusMessage, command: string): Disposable;


		/**
		 *
		 */
		  function addErrorLanguageStatus(language: any, message: string , command: CommandCallback): Disposable;
  	  function addErrorLanguageStatus(language: any, message: string , command: string): Disposable;
  	  function addErrorLanguageStatus(language: any, message: LanguageStatusMessage, command: CommandCallback): Disposable;
  	  function addErrorLanguageStatus(language: any, message: LanguageStatusMessage, command: string): Disposable;


	}

    /**
     * The ```extensions``` module contains function that are specific to extensions.
     */
	 module extensions {

		 function getStateMemento(extensionId: string, global?: boolean): Memento;

		 function getConfigurationMemento(extensionId: string): ReadOnlyMemento;

		 function getExtension(extensionId: string): any;

		 function getTelemetryInfo(): Thenable<ITelemetryInfo>;
	}

	 interface IHTMLContentElement {
		formattedText?:string;
		text?: string;
		className?: string;
		style?: string;
		customStyle?: any;
		tagName?: string;
		children?: IHTMLContentElement[];
		isText?: boolean;
	}

	// --- Begin Monaco.Modes
	 module Modes {
		 interface ILanguage {
			// required
			name: string;								// unique name to identify the language
			tokenizer: Object;							// map from string to ILanguageRule[]

			// optional
			displayName?: string;						// nice display name
			ignoreCase?: boolean;							// is the language case insensitive?
			lineComment?: string;						// used to insert/delete line comments in the editor
			blockCommentStart?: string;					// used to insert/delete block comments in the editor
			blockCommentEnd?: string;
			defaultToken?: string;						// if no match in the tokenizer assign this token class (default 'source')
			brackets?: ILanguageBracket[];				// for example [['{','}','delimiter.curly']]

			// advanced
			start?: string;								// start symbol in the tokenizer (by default the first entry is used)
			tokenPostfix?: string;						// attach this to every token class (by default '.' + name)
			autoClosingPairs?: string[][];				// for example [['"','"']]
			wordDefinition?: RegExp;					// word definition regular expression
			outdentTriggers?: string;					// characters that could potentially cause outdentation
			enhancedBrackets?: Modes.IRegexBracketPair[];// Advanced auto completion, auto indenting, and bracket matching
		}

		/**
		 * This interface can be shortened as an array, ie. ['{','}','delimiter.curly']
		 */
		 interface ILanguageBracket {
			open: string;	// open bracket
			close: string;	// closeing bracket
			token: string;	// token class
		}

		 interface ILanguageAutoComplete {
			triggers: string;				// characters that trigger auto completion rules
			match: RegExp;			// autocomplete if this matches
			complete: string;				// complete with this string
		}

		 interface ILanguageAutoIndent {
			match: RegExp; 			// auto indent if this matches on enter
			matchAfter: RegExp;		// and auto-outdent if this matches on the next line
		}

		/**
		 * Standard brackets used for auto indentation
		 */
		 interface IBracketPair {
			tokenType:string;
			open:string;
			close:string;
			isElectric:boolean;
		}

		/**
		 * Regular expression based brackets. These are always electric.
		 */
		 interface IRegexBracketPair {
			openTrigger?: string; // The character that will trigger the evaluation of 'open'.
			open: RegExp; // The definition of when an opening brace is detected. This regex is matched against the entire line upto, and including the last typed character (the trigger character).
			closeComplete?: string; // How to complete a matching open brace. Matches from 'open' will be expanded, e.g. '</$1>'
			matchCase?: boolean; // If set to true, the case of the string captured in 'open' will be detected an applied also to 'closeComplete'.
								// This is useful for cases like BEGIN/END or begin/end where the opening and closing phrases are unrelated.
								// For identical phrases, use the $1 replacement syntax above directly in closeComplete, as it will
								// include the proper casing from the captured string in 'open'.
								// Upper/Lower/Camel cases are detected. Camel case dection uses only the first two characters and assumes
								// that 'closeComplete' contains wors separated by spaces (e.g. 'End Loop')

			closeTrigger?: string; // The character that will trigger the evaluation of 'close'.
			close?: RegExp; // The definition of when a closing brace is detected. This regex is matched against the entire line upto, and including the last typed character (the trigger character).
			tokenType?: string; // The type of the token. Matches from 'open' or 'close' will be expanded, e.g. 'keyword.$1'.
							   // Only used to auto-(un)indent a closing bracket.
		}

		/**
		 * Definition of documentation comments (e.g. Javadoc/JSdoc)
		 */
		 interface IDocComment {
			scope: string; // What tokens should be used to detect a doc comment (e.g. 'comment.documentation').
			open: string; // The string that starts a doc comment (e.g. '/**')
			lineStart: string; // The string that appears at the start of each line, except the first and last (e.g. ' * ').
			close?: string; // The string that appears on the last line and closes the doc comment (e.g. ' */').
		}

		// --- Begin InplaceReplaceSupport
		/**
		 * Interface used to navigate with a value-set.
		 */
		 interface IInplaceReplaceSupport {
			sets: string[][];
		}

		interface IInplaceReplaceSupportRegister{
		 register(modeId: string, inplaceReplaceSupport: Modes.IInplaceReplaceSupport): Disposable;
	 }

		 var InplaceReplaceSupport: IInplaceReplaceSupportRegister;
		// --- End InplaceReplaceSupport


		// --- Begin TokenizationSupport
		enum Bracket {
			None = 0,
			Open = 1,
			Close = -1
		}
		// --- End TokenizationSupport

		// --- Begin IDeclarationSupport
		 interface IDeclarationSupport {
			tokens?: string[];
			findDeclaration(document: TextDocument, position: Position, token: CancellationToken): Thenable<IReference>;
		}

		interface IDeclarationSupportRegister{
		 register(modeId: string, declarationSupport: IDeclarationSupport): Disposable;
	 }
		 var DeclarationSupport: IDeclarationSupportRegister;
		// --- End IDeclarationSupport

		// --- Begin ICodeLensSupport
		 interface ICodeLensSupport {
			findCodeLensSymbols(document: TextDocument, token: CancellationToken): Thenable<ICodeLensSymbol[]>;
			findCodeLensReferences(document: TextDocument, requests: ICodeLensSymbolRequest[], token: CancellationToken): Thenable<ICodeLensReferences>;
		}
		 interface ICodeLensSymbolRequest {
			position: Position;
			languageModeStateId?: number;
		}
		 interface ICodeLensSymbol {
			range: Range;
		}
		 interface ICodeLensReferences {
			references: IReference[][];
			languageModeStateId?: number;
		}

		interface ICodeLensSupportRegister {
		 register(modeId: string, codeLensSupport: ICodeLensSupport): Disposable;
	 }

		 var CodeLensSupport: ICodeLensSupportRegister;
		// --- End ICodeLensSupport

		// --- Begin IOccurrencesSupport
		 interface IOccurrence {
			kind?:string;
			range:Range;
		}
		 interface IOccurrencesSupport {
			findOccurrences(resource: TextDocument, position: Position, token: CancellationToken): Thenable<IOccurrence[]>;
		}

		interface IOccurrencesSupportRegister{
		 register(modeId: string, occurrencesSupport:IOccurrencesSupport): Disposable;
	 }

		 var OccurrencesSupport: IOccurrencesSupportRegister;
		// --- End IOccurrencesSupport

		// --- Begin IOutlineSupport
		 interface IOutlineEntry {
			label: string;
			type: string;
			icon?: string; // icon class or null to use the default images based on the type
			range: Range;
			children?: IOutlineEntry[];
		}
		 interface IOutlineSupport {
			getOutline(document: TextDocument, token: CancellationToken): Thenable<IOutlineEntry[]>;
			outlineGroupLabel?: { [name: string]: string; };
		}

		interface IOutlineSupportRegister{
		 register(modeId: string, outlineSupport:IOutlineSupport): Disposable;
	 }

		 var OutlineSupport: IOutlineSupportRegister;
		// --- End IOutlineSupport

		// --- Begin IOutlineSupport
		 interface IQuickFix {
			label: string;
			id: any;
			score: number;
			documentation?: string;
		}

		 interface IQuickFixResult {
			edits: IResourceEdit[];
		}

		interface IQuickFixSupport {
			getQuickFixes(resource: TextDocument, marker: Range, token: CancellationToken): Thenable<IQuickFix[]>;
			runQuickFixAction(resource: TextDocument, range: Range, id: any, token: CancellationToken): Thenable<IQuickFixResult>;
		}

		interface QuickFixSupportRegister {
		 register(modeId: string, quickFixSupport:IQuickFixSupport): Disposable
	 	}
		 var QuickFixSupport: QuickFixSupportRegister
		// --- End IOutlineSupport

		// --- Begin IReferenceSupport
		 interface IReferenceSupport {
			tokens?: string[];

			/**
			 * @returns a list of reference of the symbol at the position in the
			 * 	given resource.
			 */
			findReferences(document: TextDocument, position: Position, includeDeclaration: boolean, token: CancellationToken): Thenable<IReference[]>;
		}

		interface IReferenceSupportRegister  {
		 register(modeId: string, quickFixSupport:IReferenceSupport): Disposable;
	 }
		 var ReferenceSupport: IReferenceSupportRegister;
		// --- End IReferenceSupport

		// --- Begin IParameterHintsSupport
		 interface IParameter {
			label:string;
			documentation?:string;
			signatureLabelOffset?:number;
			signatureLabelEnd?:number;
		}

		 interface ISignature {
			label:string;
			documentation?:string;
			parameters:IParameter[];
		}

		 interface IParameterHints {
			currentSignature:number;
			currentParameter:number;
			signatures:ISignature[];
		}

		 interface IParameterHintsSupport {
			/**
			 * On which characters presses should parameter hints be potentially shown.
			 */
			triggerCharacters: string[];

			/**
			 * A list of token types that prevent the parameter hints from being shown (e.g. comment, string)
			 */
			excludeTokens: string[];
			/**
			 * @returns the parameter hints for the specified position in the file.
			 */
			getParameterHints(document: TextDocument, position: Position, token: CancellationToken): Thenable<IParameterHints>;
		}

		interface IParameterHintsSupportRegister{
		 register(modeId: string, parameterHintsSupport:IParameterHintsSupport): Disposable;
	 }

		 var ParameterHintsSupport: IParameterHintsSupportRegister;
		// --- End IParameterHintsSupport

		// --- Begin IExtraInfoSupport
		 interface IComputeExtraInfoResult {
			range: Range;
			value?: string;
			htmlContent?: IHTMLContentElement[];
			className?: string;
		}
		 interface IExtraInfoSupport {
			computeInfo(document: TextDocument, position: Position, token: CancellationToken): Thenable<IComputeExtraInfoResult>;
		}
		interface IExtraInfoSupportRegister {
		 register(modeId: string, extraInfoSupport:IExtraInfoSupport): Disposable;
	 }

		 var ExtraInfoSupport: IExtraInfoSupportRegister;
		// --- End IExtraInfoSupport

		// --- Begin IRenameSupport
		 interface IRenameResult {
		    currentName: string;
		    edits: IResourceEdit[];
		    rejectReason?: string;
		}
		 interface IRenameSupport {
			filter?: string[];
			rename(document: TextDocument, position: Position, newName: string, token: CancellationToken): Thenable<IRenameResult>;
		}

		interface IRenameSupportRegister{
		 register(modeId: string, renameSupport:IRenameSupport): Disposable;
	 }
		 var RenameSupport: IRenameSupportRegister;
		// --- End IRenameSupport

		// --- Begin IFormattingSupport
		/**
		 * Interface used to format a model
		 */
		 interface IFormattingOptions {
			tabSize:number;
			insertSpaces:boolean;
		}
		/**
		 * A single edit operation, that acts as a simple replace.
		 * i.e. Replace text at `range` with `text` in model.
		 */
		 interface ISingleEditOperation {
			/**
			 * The range to replace. This can be empty to emulate a simple insert.
			 */
			range: Range;
			/**
			 * The text to replace with. This can be null to emulate a simple delete.
			 */
			text: string;
		}
		/**
		 * Supports to format source code. There are three levels
		 * on which formatting can be offered:
		 * (1) format a document
		 * (2) format a selectin
		 * (3) format on keystroke
		 */
		 interface IFormattingSupport {
			formatDocument: (document: TextDocument, options: IFormattingOptions, token: CancellationToken) => Thenable<ISingleEditOperation[]>;
			formatRange?: (document: TextDocument, range: Range, options: IFormattingOptions, token: CancellationToken) => Thenable<ISingleEditOperation[]>;
			autoFormatTriggerCharacters?: string[];
			formatAfterKeystroke?: (document: TextDocument, position: Position, ch: string, options: IFormattingOptions, token: CancellationToken) => Thenable<ISingleEditOperation[]>;
		}

		interface IFormattingSupportRegister {
		 register(modeId: string, formattingSupport:IFormattingSupport): Disposable;
	 }

		 var FormattingSupport: IFormattingSupportRegister;
		// --- End IRenameSupport

		// --- Begin ISuggestSupport
		 interface ISortingTypeAndSeparator {
			type: string;
			partSeparator?: string;
		}
		 interface IHighlight {
			start:number;
			end:number;
		}
		 interface ISuggestion {
			label: string;
			codeSnippet: string;
			type: string;
			highlights?: IHighlight[];
			typeLabel?: string;
			documentationLabel?: string;
		}
		 interface ISuggestions {
			currentWord:string;
			suggestions:ISuggestion[];
			incomplete?: boolean;
			overwriteBefore?: number;
			overwriteAfter?: number;
		}
		 interface ISuggestSupport {
			triggerCharacters: string[];
			excludeTokens: string[];

			sortBy?: ISortingTypeAndSeparator[];

			suggest: (document: TextDocument, position: Position, token: CancellationToken) => Thenable<ISuggestions[]>;
			getSuggestionDetails? : (document: TextDocument, position: Position, suggestion:ISuggestion, token: CancellationToken) => Thenable<ISuggestion>;
		}

		interface ISuggestSupportRegister {
		 register(modeId:string, suggestSupport:ISuggestSupport): Disposable;
	 }

		 var SuggestSupport: ISuggestSupportRegister;
		// --- End ISuggestSupport

		// --- Start INavigateTypesSupport

		 interface ITypeBearing {
			containerName: string;
			name: string;
			parameters: string;
			type: string;
			range: Range;
			resourceUri: Uri;
		}

		 interface INavigateTypesSupport {
			getNavigateToItems:(search: string, token: CancellationToken) => Thenable<ITypeBearing[]>;
		}

		interface INavigateTypesSupport{
		 register(modeId:string, navigateTypeSupport:INavigateTypesSupport): Disposable;
	 }

		 var NavigateTypesSupport: INavigateTypesSupport;

		// --- End INavigateTypesSupport

		// --- Begin ICommentsSupport
		 interface ICommentsSupport {
			commentsConfiguration: ICommentsConfiguration;
		}
		 interface ICommentsConfiguration {
			lineCommentTokens?:string[];
			blockCommentStartToken?:string;
			blockCommentEndToken?:string;
		}

		interface ICommentsConfigurationRegister{
		 register(modeId:string, commentsSupport:ICommentsSupport): Disposable;
	 }

		 var CommentsSupport: ICommentsConfigurationRegister;
		// --- End ICommentsSupport

		// --- Begin ITokenTypeClassificationSupport
		 interface ITokenTypeClassificationSupport {
			wordDefinition?: RegExp;
		}
		interface ITokenTypeClassificationSupportRegister{
		 register(modeId:string, tokenTypeClassificationSupport:ITokenTypeClassificationSupport): Disposable;
	 }

		 var TokenTypeClassificationSupport: ITokenTypeClassificationSupportRegister;
		// --- End ITokenTypeClassificationSupport

		// --- Begin IElectricCharacterSupport
		 interface IElectricCharacterSupport {
			brackets: IBracketPair[];
			regexBrackets?: IRegexBracketPair[];
			docComment?: IDocComment;
			caseInsensitive?: boolean;
			embeddedElectricCharacters?: string[];
		}

		interface IElectricCharacterSupportRegister {
		 register(modeId:string, electricCharacterSupport:IElectricCharacterSupport): Disposable;
	 }

		 var ElectricCharacterSupport: IElectricCharacterSupportRegister;
		// --- End IElectricCharacterSupport

		// --- Begin ICharacterPairSupport
		 interface ICharacterPairSupport {
			autoClosingPairs: IAutoClosingPairConditional[];
			surroundingPairs?: IAutoClosingPair[];
		}
		/**
		 * Interface used to support insertion of matching characters like brackets and qoutes.
		 */
		 interface IAutoClosingPair {
			open:string;
			close:string;
		}
		 interface IAutoClosingPairConditional extends IAutoClosingPair {
			notIn?: string[];
		}

		interface IAutoClosingPairConditionalRegister {
		 register(modeId:string, characterPairSupport:ICharacterPairSupport): Disposable;
	 }
		 var CharacterPairSupport: IAutoClosingPairConditionalRegister;
		// --- End ICharacterPairSupport

		// --- Begin IOnEnterSupport
		 interface IBracketPair2 {
			open: string;
			close: string;
		}
		 interface IIndentationRules {
			decreaseIndentPattern: RegExp;
			increaseIndentPattern: RegExp;
			indentNextLinePattern?: RegExp;
			unIndentedLinePattern?: RegExp;
		}
		 enum IndentAction {
			None,
			Indent,
			IndentOutdent,
			Outdent
		}
		 interface IEnterAction {
			indentAction:IndentAction;
			appendText?:string;
			removeText?:number;
		}
		 interface IOnEnterRegExpRules {
			beforeText: RegExp;
			afterText?: RegExp;
			action: IEnterAction;
		}
		 interface IOnEnterSupportOptions {
			brackets?: IBracketPair2[];
			indentationRules?: IIndentationRules;
			regExpRules?: IOnEnterRegExpRules[];
		}

		interface IOnEnterSupportOptionsRegister{
		 register(modeId:string, opts:IOnEnterSupportOptions): Disposable;
	 }

		 var OnEnterSupport: IOnEnterSupportOptionsRegister;
		// --- End IOnEnterSupport

		 interface IResourceEdit {
			resource: Uri;
			range?: Range;
			newText: string;
		}

		 interface IReference {
			resource: Uri;
			range: Range;
		}

		 interface IMode {
			getId(): string;
		}

		 interface IWorker<T> {
			disposable: Disposable;
			load(): Thenable<T>;
		}

		function registerMonarchDefinition(modeId: string, language: Modes.ILanguage): Disposable;
		function loadInBackgroundWorker<T>(scriptSrc: string): IWorker<T>;

	}


}

/**
 * Thenable is a common denominator between ES6 promises, Q, jquery.Deferred, WinJS.Promise,
 * and others. This API makes no assumption about what promise libary is being used which
 * enables reusing existing code without migrating to a specific promise implementation. Still,
 * we recommand the use of native promises which are available in VS Code.
 */
interface Thenable<R> {
    /**
    * Attaches callbacks for the resolution and/or rejection of the Promise.
    * @param onfulfilled The callback to execute when the Promise is resolved.
    * @param onrejected The callback to execute when the Promise is rejected.
    * @returns A Promise for the completion of which ever callback is executed.
    */
    then<TResult>(onfulfilled?: (value: R) => TResult, onrejected?: (reason: any) => TResult): Thenable<TResult>;
		then<TResult>(onfulfilled?: (value: R) => TResult, onrejected?: (reason: any) => Thenable<TResult>): Thenable<TResult>;
		then<TResult>(onfulfilled?: (value: R) => Thenable<TResult>, onrejected?: (reason: any) => TResult ): Thenable<TResult>;
		then<TResult>(onfulfilled?: (value: R) => Thenable<TResult>, onrejected?: (reason: any) => Thenable<TResult>): Thenable<TResult>;
		then<TResult>(onfulfilled?: (value: R) => Thenable<TResult>, onrejected?: (reason: any) => void): Thenable<TResult>;
		then<TResult>(onfulfilled?: (value: R) => TResult, onrejected?: (reason: any) => void): Thenable<TResult>;
}

// ---- ES6 promise ------------------------------------------------------

/**
 * Represents the completion of an asynchronous operation
 */
interface Promise<T> extends Thenable<T> {
    /**
    * Attaches callbacks for the resolution and/or rejection of the Promise.
    * @param onfulfilled The callback to execute when the Promise is resolved.
    * @param onrejected The callback to execute when the Promise is rejected.
    * @returns A Promise for the completion of which ever callback is executed.
    */
		then<TResult>(onfulfilled?: (value: T) => TResult, onrejected?: (reason: any) => TResult): Thenable<TResult>;
		then<TResult>(onfulfilled?: (value: T) => TResult, onrejected?: (reason: any) => Thenable<TResult>): Thenable<TResult>;
		then<TResult>(onfulfilled?: (value: T) => Thenable<TResult>, onrejected?: (reason: any) => TResult ): Thenable<TResult>;
		then<TResult>(onfulfilled?: (value: T) => Thenable<TResult>, onrejected?: (reason: any) => Thenable<TResult>): Thenable<TResult>;
		then<TResult>(onfulfilled?: (value: T) => Thenable<TResult>, onrejected?: (reason: any) => void): Thenable<TResult>;
		then<TResult>(onfulfilled?: (value: T) => TResult, onrejected?: (reason: any) => void): Thenable<TResult>;

    /**
     * Attaches a callback for only the rejection of the Promise.
     * @param onrejected The callback to execute when the Promise is rejected.
     * @returns A Promise for the completion of the callback.
     */
    catch(onrejected?: (reason: any) => T): Promise<T>;
		catch(onrejected?: (reason: any) => Thenable<T>): Promise<T>;

    // [Symbol.toStringTag]: string;
}

interface PromiseConstructor {
    // /**
    //   * A reference to the prototype.
    //   */
    // prototype: Promise<any>;

    /**
     * Creates a new Promise.
     * @param executor A callback used to initialize the promise. This callback is passed two arguments:
     * a resolve callback used resolve the promise with a value or the result of another promise,
     * and a reject callback used to reject the promise with a provided reason or error.
     */
    new <T>(executor: (resolve: (value?: T) => void, reject: (reason?: any) => void) => void): Promise<T>;
		new <T>(executor: (resolve: (value?: Thenable<T>) => void, reject: (reason?: any) => void) => void): Promise<T>;

		/**
     * Creates a Promise that is resolved with an array of results when all of the provided Promises
     * resolve, or rejected when any Promise is rejected.
     * @param values An array of Promises.
     * @returns A new Promise.
     */
    all<T>(values: Array<T>): Promise<T[]>;
		all<T>(values: Array<Thenable<T>>): Promise<T[]>;

    /**
     * Creates a Promise that is resolved or rejected when any of the provided Promises are resolved
     * or rejected.
     * @param values An array of Promises.
     * @returns A new Promise.
     */
    race<T>(values: Array<Thenable<T>>): Promise<T>;
		race<T>(values: Array<T>): Promise<T>;

    /**
     * Creates a new rejected promise for the provided reason.
     * @param reason The reason the promise was rejected.
     * @returns A new rejected Promise.
     */
    reject(reason: any): Promise<void>;

    /**
     * Creates a new rejected promise for the provided reason.
     * @param reason The reason the promise was rejected.
     * @returns A new rejected Promise.
     */
    reject<T>(reason: any): Promise<T>;

    /**
      * Creates a new resolved promise for the provided value.
      * @param value A promise.
      * @returns A promise whose internal state matches the provided promise.
      */
    resolve<T>(value: Thenable<T>): Promise<T>;
		resolve<T>(value: T): Promise<T>;
    /**
     * Creates a new resolved promise .
     * @returns A resolved promise.
     */
    resolve(): Promise<void>;

    // [Symbol.species]: Function;
}

declare var Promise: PromiseConstructor;

// TS 1.6 & node_module
// = vscode;

// declare module 'vscode' {
//      = vscode;
// }

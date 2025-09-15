﻿namespace Ionide.VSCode.FSharp

[<ReflectedDefinition>]
module DTO =

    module LSP =
        type Position = { line: int; character: int }
        type Range = { start: Position; ``end``: Position }

    type Pos = { Line: int; Column: int }

    type ParseRequest =
        { FileName: string
          IsAsync: bool
          Lines: string[]
          Version: int }

    type ProjectRequest = { FileName: string }

    type DeclarationsRequest =
        { FileName: string
          Lines: string[]
          Version: int }

    type HelptextRequest = { Symbol: string }

    type PositionRequest =
        { FileName: string
          Line: int
          Column: int
          Filter: string }

    type CompletionRequest =
        { FileName: string
          SourceLine: string
          Line: int
          Column: int
          Filter: string
          IncludeKeywords: bool
          IncludeExternal: bool
          Version: int }

    type WorkspacePeekRequest =
        { Directory: string
          Deep: int
          ExcludedDirs: string[] }

    type RangesAtPositionRequest = { FileName: string; Positions: Pos[] }

    type WorkspaceLoadRequest =
        { Files: string[]
          DisableInMemoryProjectReferences: bool }

    type DocumentationForSymbolRequest = { XmlSig: string; Assembly: string }

    type OverloadSignature = { Signature: string; Comment: string }

    type TooltipSignature =
        { Signature: string
          Comment: string
          Footer: string }

    type DocumentationDescription =
        { XmlKey: string
          Constructors: string list
          Fields: string list
          Functions: string list
          Interfaces: string list
          Attributes: string list
          DeclaredTypes: string list
          Signature: string
          Comment: string
          FooterLines: string list }

    type Error =
        {
            /// 1-indexed first line of the error block
            StartLine: int
            /// 1-indexed first column of the error block
            StartColumn: int
            /// 1-indexed last line of the error block
            EndLine: int
            /// 1-indexed last column of the error block
            EndColumn: int
            /// Description of the error
            Message: string
            ///Severity of the error - warning or error
            Severity: string
            /// Type of the Error
            Subcategory: string
            ///File Name
            FileName: string
        }

    type ErrorResp = { File: string; Errors: Error[] }

    type Declaration =
        { File: string; Line: int; Column: int }

    type Completion =
        { Name: string
          ReplacementText: string
          Glyph: string
          GlyphChar: string
          NamespaceToOpen: string }

    type SymbolUse =
        { FileName: string
          StartLine: int
          StartColumn: int
          EndLine: int
          EndColumn: int
          IsFromDefinition: bool
          IsFromAttribute: bool
          IsFromComputationExpression: bool
          IsFromDispatchSlotImplementation: bool
          IsFromPattern: bool
          IsFromType: bool }

    type SymbolUses = { Name: string; Uses: SymbolUse array }

    type AdditionalEdit =
        { Text: string
          Line: int
          Column: int
          Type: string }

    type Helptext =
        { Name: string
          Overloads: OverloadSignature[][]
          AdditionalEdit: AdditionalEdit }

    type OverloadParameter =
        { Name: string
          CanonicalTypeTextForSorting: string
          Display: string
          Description: string }

    type Overload =
        { Tip: OverloadSignature[][]
          TypeText: string
          Parameters: OverloadParameter[]
          IsStaticArguments: bool }

    type Method =
        { Name: string
          CurrentParameter: int
          Overloads: Overload[] }

    type Range =
        { StartColumn: int
          StartLine: int
          EndColumn: int
          EndLine: int }


    type Symbol =
        { UniqueName: string
          Name: string
          Glyph: string
          GlyphChar: string
          IsTopLevel: bool
          Range: Range
          BodyRange: Range
          File: string
          EnclosingEntity: string
          IsAbstract: bool }

    type Symbols =
        { Declaration: Symbol
          Nested: Symbol[] }

    type Fix =
        { FromRange: Range
          FromText: string
          ToText: string }

    type Lint =
        { Info: string
          Input: string
          Range: Range
          Fix: Fix }

    type AnalyzerMsg =
        { Type: string
          Message: string
          Code: string
          Severity: string
          Range: Range
          Fixes: Fix[] }

    type AnalyzerResponse =
        { File: string
          Messages: AnalyzerMsg[] }

    type ProjectFilePath = string
    type SourceFilePath = string
    type ResolvedReferencePath = string

    type ProjectLoading = { Project: ProjectFilePath }

    type ProjectResponseInfoDotnetSdk =
        { IsTestProject: bool
          Configuration: string
          IsPackable: bool
          TargetFramework: string
          TargetFrameworkIdentifier: string
          TargetFrameworkVersion: string
          RestoreSuccess: bool
          TargetFrameworks: string list
          RunCmd: RunCmd option
          IsPublishable: bool option }

    and [<RequireQualifiedAccess>] RunCmd = { Command: string; Arguments: string }

    type ProjectResponseItem =
        { Name: string
          FilePath: string
          VirtualPath: string
          Metadata: Map<string, string> }

    type ProjectReference =
        { RelativePath: string
          ProjectFileName: string }

    type PackageReference =
        { Name: string
          Version: string
          FullPath: string }

    type Project =
        { Project: ProjectFilePath
          Files: SourceFilePath array
          Output: string
          References: ResolvedReferencePath array
          ProjectReferences: ProjectReference array
          PackageReferences: PackageReference array
          Logs: Map<string, string>
          OutputType: string
          Info: ProjectResponseInfoDotnetSdk
          Items: ProjectResponseItem array
          AdditionalInfo: Map<string, string> }

    type FsdnRequest = { Signature: string }

    type OpenNamespace =
        { Namespace: string
          Name: string
          Type: string
          Line: int
          Column: int
          MultipleNames: bool }

    type QualifySymbol = { Name: string; Qualifier: string }

    type ResolveNamespace =
        { Opens: OpenNamespace[]
          Qualifies: QualifySymbol[]
          Word: string }

    type UnionCaseGenerator = { Text: string; Position: Pos }

    type RecordStubCaseGenerator = { Text: string; Position: Pos }

    type InterfaceStubGenerator = { Text: string; Position: Pos }

    type Parameter = { Name: string; Type: string }

    type SignatureData =
        { OutputType: string
          Parameters: Parameter list list
          Generics: string list }

    type RangesAtPosition = { Ranges: Range list list }

    type WorkspacePeek = { Found: WorkspacePeekFound[] }

    and WorkspacePeekFound =
        | Directory of WorkspacePeekFoundDirectory
        | Solution of WorkspacePeekFoundSolution

    and WorkspacePeekFoundDirectory =
        { Directory: string; Fsprojs: string[] }

    and WorkspacePeekFoundSolution =
        { Path: string
          Items: WorkspacePeekFoundSolutionItem[]
          Configurations: WorkspacePeekFoundSolutionConfiguration[] }

    and [<RequireQualifiedAccess>] WorkspacePeekFoundSolutionItem =
        { Guid: string
          Name: string
          Kind: WorkspacePeekFoundSolutionItemKind }

    and WorkspacePeekFoundSolutionItemKind =
        | MsbuildFormat of WorkspacePeekFoundSolutionItemKindMsbuildFormat
        | Folder of WorkspacePeekFoundSolutionItemKindFolder

    and [<RequireQualifiedAccess>] WorkspacePeekFoundSolutionItemKindMsbuildFormat =
        { Configurations: WorkspacePeekFoundSolutionConfiguration[] }

    and [<RequireQualifiedAccess>] WorkspacePeekFoundSolutionItemKindFolder =
        { Items: WorkspacePeekFoundSolutionItem[]
          Files: string[] }

    and [<RequireQualifiedAccess>] WorkspacePeekFoundSolutionConfiguration =
        { Id: string
          ConfigurationName: string
          PlatformName: string }

    type FsdnResponse = { Functions: string[] }

    type HighlightingRange =
        { range: Fable.Import.VSCode.Vscode.Range
          tokenType: string }

    type HighlightingResponse = { highlights: HighlightingRange[] }

    type ResponseError<'T> =
        { Code: int
          Message: string
          AdditionalData: 'T }

    [<RequireQualifiedAccess>]
    type ErrorCodes =
        | GenericError = 1
        | ProjectNotRestored = 100
        | ProjectParsingFailed = 101
        | LanguageNotSupported = 102

    module ErrorDataTypes =
        type ProjectNotRestoredData = { Project: ProjectFilePath }
        type ProjectParsingFailedData = { Project: ProjectFilePath }
        type LanguageNotSupportedData = { Project: ProjectFilePath }

    [<RequireQualifiedAccess>]
    type ErrorData =
        | GenericError
        | ProjectNotRestored of ErrorDataTypes.ProjectNotRestoredData
        | ProjectParsingFailed of ErrorDataTypes.ProjectParsingFailedData
        | LangugageNotSupported of ErrorDataTypes.LanguageNotSupportedData

    type UnusedDeclaration = { Range: Range; IsThisMember: bool }

    type UnusedDeclarations = { Declarations: UnusedDeclaration[] }

    type UnusedOpens = { Declarations: Range[] }

    type SimplifiedNameData =
        { RelativeName: string
          UnnecessaryRange: Range }

    type SimplifiedName = { Names: SimplifiedNameData[] }

    type CompileData = { Code: int }

    type PipelineHint =
        { Line: int
          Types: string[]
          PrecedingNonPipeExprLine: int option }

    type TestAdapterEntry =
        { name: string
          range: Fable.Import.VSCode.Vscode.Range
          childs: TestAdapterEntry[]
          id: int
          list: bool
          ``type``: string
          moduleType: string }

    type TestForFile =
        { file: string
          tests: TestAdapterEntry[] }

    type NestedLanguagesForFile =
        { textDocument: {| uri: string; version: int |}
          nestedLanguages:
              {| language: string
                 ranges: LSP.Range[] |}[] }

    type Result<'T> = { Kind: string; Data: 'T }

    type HelptextResult = Result<Helptext>
    type CompletionResult = Result<Completion[]>
    type SymbolUseResult = Result<SymbolUses>
    type TooltipResult = Result<TooltipSignature[][]>
    type ParseResult = Result<ErrorResp>
    type FindDeclarationResult = Result<Declaration>
    type MethodResult = Result<Method>
    type DeclarationResult = Result<Symbols[]>
    type LintResult = Result<Lint[]>
    type ProjectResult = Result<Project>
    type ProjectLoadingResult = Result<ProjectLoading>
    type ResolveNamespaceResult = Result<ResolveNamespace>
    type UnionCaseGeneratorResult = Result<UnionCaseGenerator>
    type SignatureDataResult = Result<SignatureData>
    type WorkspacePeekResult = Result<WorkspacePeek>
    type UnusedOpensResult = Result<UnusedOpens>
    type UnusedDeclarationsResult = Result<UnusedDeclarations>
    type SimplifiedNameResult = Result<SimplifiedName>
    type RangesAtPositionsResult = Result<RangesAtPosition>
    type CompileResult = Result<CompileData>
    type AnalyzerResult = Result<AnalyzerResponse>
    type FsdnResult = Result<FsdnResponse>
    type HighlightingResult = Result<HighlightingResponse>
    type FSharpLiterateResult = Result<string>
    type PipelineHintsResult = Result<PipelineHint array>
    type TestResult = Result<TestForFile>


    module DotnetNew =
        type Template =
            { Name: string
              ShortName: string
              Tags: string list }

        type DotnetNewListRequest = { Query: string }

        type DotnetNewRunRequest =
            { Template: string
              Output: string option
              Name: string option }

        type DotnetNewListResponse = Result<Template list>

        type DotnetNewRunResponse = Result<string>

    module FsProj =
        type DotnetProjectRequest = { Target: string; Reference: string }

        type DotnetFileRequest =
            { FsProj: string
              FileVirtualPath: string }

        type DotnetFile2Request =
            { FsProj: string
              FileVirtualPath: string
              NewFile: string }

        type DotnetRenameFileRequest =
            { FsProj: string
              OldFileVirtualPath: string
              NewFileName: string }

namespace Ionide.VSCode.FSharp

[<ReflectedDefinition>]
module DTO =
    type ParseRequest = { FileName : string; IsAsync : bool; Lines : string[]; Version : int }
    type ProjectRequest = { FileName : string}
    type DeclarationsRequest = {FileName : string; Lines : string[]; Version :int}
    type HelptextRequest = {Symbol : string}
    type PositionRequest = {FileName : string; Line : int; Column : int; Filter : string}
    type CompletionRequest = {FileName : string; SourceLine : string; Line : int; Column : int; Filter : string; IncludeKeywords : bool; IncludeExternal: bool}
    type WorkspacePeekRequest = {Directory: string; Deep: int; ExcludedDirs: string[] }
    type WorkspaceLoadRequest = { Files: string[] }

    type OverloadSignature = {
        Signature: string
        Comment: string
    }

    type TooltipSignature = {
        Signature: string
        Comment: string
        Footer: string
    }

    type Error = {
        /// 1-indexed first line of the error block
        StartLine : int
        /// 1-indexed first column of the error block
        StartColumn : int
        /// 1-indexed last line of the error block
        EndLine : int
        /// 1-indexed last column of the error block
        EndColumn : int
        /// Description of the error
        Message : string
        ///Severity of the error - warning or error
        Severity : string
        /// Type of the Error
        Subcategory : string
        ///File Name
        FileName : string
        }

    type ErrorResp = {
        File : string
        Errors : Error []
    }

    type Declaration = {
        File : string
        Line : int
        Column : int
    }

    type Completion = {
        Name : string
        ReplacementText : string
        Glyph : string
        GlyphChar: string
        NamespaceToOpen: string
    }

    type SymbolUse = {
      FileName : string
      StartLine : int
      StartColumn : int
      EndLine : int
      EndColumn : int
      IsFromDefinition : bool
      IsFromAttribute : bool
      IsFromComputationExpression : bool
      IsFromDispatchSlotImplementation : bool
      IsFromPattern : bool
      IsFromType : bool
    }

    type SymbolUses = {
        Name : string
        Uses : SymbolUse array
    }

    type AdditionalEdit = {
      Text: string
      Line: int
      Column: int
    }

    type Helptext = {
        Name : string
        Overloads: OverloadSignature [] []
        AdditionalEdit: AdditionalEdit
    }

    type OverloadParameter = {
        Name : string
        CanonicalTypeTextForSorting : string
        Display : string
        Description : string
    }
    type Overload = {
        Tip : OverloadSignature [] []
        TypeText : string
        Parameters : OverloadParameter []
        IsStaticArguments : bool
    }

    type Method = {
      Name : string
      CurrentParameter : int
      Overloads : Overload []
    }

    type CompilerLocation = {
        Fcs : string
        Fsi : string
        MSBuild : string
    }

    type Range = {
        StartColumn: int
        StartLine: int
        EndColumn: int
        EndLine: int
    }

    type Pos = {
        Line: int
        Col: int
    }

    type Symbol = {
        UniqueName: string
        Name: string
        Glyph: string
        GlyphChar: string
        IsTopLevel: bool
        Range: Range
        BodyRange : Range
        File : string
        EnclosingEntity : string
        IsAbstract : bool
    }

    type Symbols = {
        Declaration : Symbol;
        Nested : Symbol []
    }

    type Fix = {
        FromRange: Range
        FromText: string
        ToText: string
    }

    type Lint = {
        Info : string
        Input : string
        Range : Range
        Fix: Fix
    }

    type ProjectFilePath = string
    type SourceFilePath = string
    type ProjectReferencePath = string

    type ProjectLoading = {
        Project: ProjectFilePath
    }

    [<RequireQualifiedAccess>]
    type ProjectResponseInfo =
      | DotnetSdk of ProjectResponseInfoDotnetSdk
      | Verbose
      | ProjectJson
    and ProjectResponseInfoDotnetSdk = {
      IsTestProject: bool
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

    type Project = {
        Project: ProjectFilePath
        Files: SourceFilePath list
        Output: string
        References: ProjectReferencePath list
        Logs: Map<string, string>
        OutputType: string
        Info: ProjectResponseInfo
        AdditionalInfo: Map<string, string>
    }

    type OpenNamespace = {
        Namespace : string
        Name : string
        Type : string
        Line : int
        Column : int
        MultipleNames : bool
    }

    type QualifySymbol = {
        Name : string
        Qualifier : string
    }

    type ResolveNamespace = {
        Opens : OpenNamespace []
        Qualifies: QualifySymbol []
        Word : string
    }

    type UnionCaseGenerator = {
        Text : string
        Position : Pos
    }

    type Parameter = {
        Name : string
        Type : string
    }

    type SignatureData = {
        OutputType : string
        Parameters : Parameter list list
    }

    type WorkspacePeek = {
        Found : WorkspacePeekFound []
    }
    and WorkspacePeekFound =
      | Directory of WorkspacePeekFoundDirectory
      | Solution of WorkspacePeekFoundSolution
    and WorkspacePeekFoundDirectory = {
        Directory: string
        Fsprojs: string []
    }
    and WorkspacePeekFoundSolution = {
        Path: string
        Items: WorkspacePeekFoundSolutionItem []
        Configurations: WorkspacePeekFoundSolutionConfiguration []
    }
    and [<RequireQualifiedAccess>] WorkspacePeekFoundSolutionItem = {
        Guid: string
        Name: string
        Kind: WorkspacePeekFoundSolutionItemKind
    }
    and WorkspacePeekFoundSolutionItemKind =
        | MsbuildFormat of WorkspacePeekFoundSolutionItemKindMsbuildFormat
        | Folder of WorkspacePeekFoundSolutionItemKindFolder
    and [<RequireQualifiedAccess>] WorkspacePeekFoundSolutionItemKindMsbuildFormat = {
        Configurations: WorkspacePeekFoundSolutionConfiguration []
    }
    and [<RequireQualifiedAccess>] WorkspacePeekFoundSolutionItemKindFolder = {
        Items: WorkspacePeekFoundSolutionItem []
        Files: string []
    }
    and [<RequireQualifiedAccess>] WorkspacePeekFoundSolutionConfiguration = {
        Id: string
        ConfigurationName: string
        PlatformName: string
    }

    type ResponseError<'T> = {
        Code: int
        Message: string
        AdditionalData: 'T
    }

    [<RequireQualifiedAccess>]
    type ErrorCodes =
       | GenericError = 1
       | ProjectNotRestored = 100
       | ProjectParsingFailed = 101

    module ErrorDataTypes =
        type ProjectNotRestoredData = { Project: ProjectFilePath }
        type ProjectParsingFailedData = { Project: ProjectFilePath }

    [<RequireQualifiedAccess>]
    type ErrorData =
       | GenericError
       | ProjectNotRestored of ErrorDataTypes.ProjectNotRestoredData
       | ProjectParsingFailed of ErrorDataTypes.ProjectParsingFailedData

    type UnusedDeclaration = {
        Range: Range
        IsThisMember: bool
    }

    type UnusedDeclarations = {
        Declarations : UnusedDeclaration []
    }

    type UnusedOpens = {
        Declarations : Range []
    }

    type SimplifiedNameData = {
        RelativeName : string
        UnnecessaryRange: Range
    }

    type SimplifiedName = {
        Names: SimplifiedNameData []
    }

    type Result<'T> = {Kind : string; Data : 'T}
    type CompilerLocationResult = Result<CompilerLocation>
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


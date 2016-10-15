namespace Ionide.VSCode.FSharp

open Fable.Import.vscode
    

module CodeRange =
    type CodeRange = Fable.Import.vscode.Range
    
    let fromDTO (range: DTO.Range) : CodeRange =
        CodeRange (float range.StartLine - 1.,
                   float range.StartColumn - 1.,
                   float range.EndLine - 1.,
                   float range.EndColumn - 1.)

    let fromDeclaration (decl: DTO.Declaration) (length: float) : CodeRange =
        CodeRange (float decl.Line - 1.,
                   float decl.Column - 1.,
                   float decl.Line - 1.,
                   float decl.Column + length - 1.)

    let fromSymbolUse (su: DTO.SymbolUse) : CodeRange =
        CodeRange (float su.StartLine - 1.,
                   float su.StartColumn - 1.,
                   float su.EndLine - 1.,
                   float su.EndColumn - 1.)
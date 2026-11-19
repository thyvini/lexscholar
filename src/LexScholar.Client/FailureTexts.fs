module LexScholar.Client.FailureTexts

let library = function
    | Library.NotAPdf -> Texts.notPdf
    | Library.CorruptPdf -> Texts.corruptPdf
    | Library.EncryptedPdf -> Texts.encryptedPdf
    | Library.TooLarge -> Texts.tooLarge
    | Library.TooManyPages -> Texts.tooManyPages
    | Library.DuplicateAssociation -> Texts.duplicateAssociation
    | Library.InvalidRole -> Texts.invalidRole
    | Library.NotFound -> Texts.missingProjectOrDocument
    | Library.DocumentNotFound -> Texts.missingDocument
    | Library.RequestFailed -> Texts.genericFailure

let project = function
    | Projects.InvalidName -> Texts.invalidProjectName
    | Projects.DuplicateName -> Texts.duplicateProjectName
    | Projects.NotFound -> Texts.missingProject
    | Projects.RequestFailed -> Texts.genericFailure

module LexScholar.Client.FailureTexts

let export = function
    | Library.EmptyExport -> Texts.emptyExport
    | Library.ExportDocumentUnavailable name -> Texts.exportUnavailable name
    | Library.ExportRequestFailed -> Texts.exportFailed

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

let summary = function
    | Summary.NotConfigured -> Texts.summaryNotConfigured
    | Summary.Unavailable -> Texts.summaryUnavailable
    | Summary.NoText -> Texts.summaryNoText
    | Summary.NotEnoughText -> Texts.summaryNotEnoughText
    | Summary.TooManyPages -> Texts.summaryTooManyPages
    | Summary.UsageLimitReached -> Texts.summaryUsageLimit
    | Summary.SignedOut -> Texts.summarySignedOut
    | Summary.NoPlanAccess -> Texts.summaryNoPlan
    | Summary.DestinationRequired -> Texts.summaryDestination
    | Summary.ServiceFailed _ | Summary.RequestFailed | Summary.ConfirmationRequired _ -> Texts.genericFailure

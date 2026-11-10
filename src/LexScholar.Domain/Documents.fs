namespace LexScholar.Domain

open System

type DocumentRole =
    | FocusedSource
    | ReferenceSource

/// One shared library record for a byte-exact PDF. Sha256 narrows duplicate
/// candidates and is not unique; equality of documents is decided by bytes.
type LibraryDocument =
    { Id: Guid
      Sha256: string
      StorageKey: string
      OriginalFileName: string
      ByteLength: int64
      PageCount: int
      ImportedUtc: DateTimeOffset }

/// A project's association with a shared document; keyed by (ProjectId, DocumentId).
type ProjectDocument =
    { ProjectId: Guid
      DocumentId: Guid
      Role: DocumentRole
      AddedUtc: DateTimeOffset }

type DocumentError =
    | InvalidSha256
    | InvalidStorageKey
    | NonPositiveByteLength
    | NonPositivePageCount

module LibraryDocument =
    let private isLowercaseHex (character: char) =
        (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')

    let private isSha256 (value: string) =
        not (isNull value) && value.Length = 64 && Seq.forall isLowercaseHex value

    // Configured import limits are deliberately not checked here: lowering them
    // must not invalidate documents that were accepted earlier.
    let create (id: Guid) (sha256: string) (storageKey: string) (originalFileName: string)
               (byteLength: int64) (pageCount: int) (importedUtc: DateTimeOffset)
               : Result<LibraryDocument, DocumentError> =
        if not (isSha256 sha256) then Error InvalidSha256
        elif String.IsNullOrWhiteSpace(storageKey) then Error InvalidStorageKey
        elif byteLength <= 0L then Error NonPositiveByteLength
        elif pageCount <= 0 then Error NonPositivePageCount
        else
            Ok { Id = id
                 Sha256 = sha256
                 StorageKey = storageKey
                 OriginalFileName = originalFileName
                 ByteLength = byteLength
                 PageCount = pageCount
                 ImportedUtc = importedUtc }

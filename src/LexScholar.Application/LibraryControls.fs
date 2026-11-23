namespace LexScholar.Application

open System

/// The durable phases needed to resume a confirmed library deletion.
[<RequireQualifiedAccess>]
type DeletionPhase =
    | Pending
    | MetadataRemoved

/// A journal item is complete only after its managed original is absent.
[<RequireQualifiedAccess>]
type DeletionStatus =
    | Pending
    | MetadataRemoved
    | Deleted
    | Incomplete

type DeletionOperation = { Id: Guid; CreatedUtc: DateTimeOffset }

type DeletionItem =
    { OperationId: Guid
      DocumentId: Guid
      OriginalFileName: string
      StorageKey: string
      Status: DeletionStatus
      Phase: DeletionPhase
      Reason: string option }

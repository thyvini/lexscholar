namespace LexScholar.Infrastructure.Migrations

open Microsoft.EntityFrameworkCore.Infrastructure
open Microsoft.EntityFrameworkCore.Migrations
open LexScholar.Infrastructure

[<DbContext(typeof<WorkspaceDb>); Migration("20261123000000_LibraryDeletions")>]
type LibraryDeletions() =
    inherit Migration()
    override _.Up(migration: MigrationBuilder) =
        migration.Sql("""
CREATE TABLE DeletionOperations (
    Id TEXT NOT NULL PRIMARY KEY,
    CreatedUtc TEXT NOT NULL
);
CREATE TABLE DeletionItems (
    OperationId TEXT NOT NULL,
    DocumentId TEXT NOT NULL,
    OriginalFileName TEXT NOT NULL,
    StorageKey TEXT NOT NULL,
    Status TEXT NOT NULL CONSTRAINT CK_DeletionItems_Status CHECK (Status IN ('pending', 'metadata_removed', 'deleted', 'incomplete')),
    Phase TEXT NOT NULL CONSTRAINT CK_DeletionItems_Phase CHECK (Phase IN ('pending', 'metadata_removed')),
    Reason TEXT NULL,
    CONSTRAINT CK_DeletionItems_StatusPhase CHECK ((Status = 'pending' AND Phase = 'pending') OR (Status IN ('metadata_removed', 'deleted') AND Phase = 'metadata_removed') OR Status = 'incomplete'),
    PRIMARY KEY (OperationId, DocumentId),
    FOREIGN KEY (OperationId) REFERENCES DeletionOperations (Id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IX_DeletionItems_DocumentId ON DeletionItems (DocumentId) WHERE Status <> 'deleted';
        """) |> ignore
    override _.Down(migration: MigrationBuilder) =
        migration.Sql("DROP TABLE DeletionItems; DROP TABLE DeletionOperations;") |> ignore

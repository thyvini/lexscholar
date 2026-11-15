namespace LexScholar.Infrastructure.Migrations

open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Infrastructure
open Microsoft.EntityFrameworkCore.Migrations
open LexScholar.Infrastructure

[<DbContext(typeof<WorkspaceDb>); Migration("20261112000000_DocumentSummaries")>]
type DocumentSummaries() =
    inherit Migration()
    override _.Up(migration: MigrationBuilder) =
        migration.Sql("""
CREATE TABLE DocumentSummaries (
    ProjectId TEXT NOT NULL,
    DocumentId TEXT NOT NULL,
    Content TEXT NOT NULL,
    GeneratedAt TEXT NOT NULL,
    PRIMARY KEY (ProjectId, DocumentId),
    FOREIGN KEY (ProjectId, DocumentId) REFERENCES ProjectDocuments (ProjectId, DocumentId) ON DELETE CASCADE
);
        """) |> ignore
    override _.Down(migration: MigrationBuilder) =
        migration.Sql("DROP TABLE DocumentSummaries;") |> ignore

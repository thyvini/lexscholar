namespace LexScholar.Infrastructure.Migrations

open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Infrastructure
open Microsoft.EntityFrameworkCore.Metadata.Builders
open Microsoft.EntityFrameworkCore.Migrations
open LexScholar.Infrastructure

[<DbContext(typeof<WorkspaceDb>); Migration("20261111000000_Documents")>]
type Documents() =
    inherit Migration()
    override _.Up(migration: MigrationBuilder) =
        migration.Sql("""
CREATE TABLE LibraryDocuments (
    Id TEXT NOT NULL PRIMARY KEY,
    Sha256 TEXT NOT NULL,
    StorageKey TEXT NOT NULL,
    OriginalFileName TEXT NOT NULL,
    ByteLength INTEGER NOT NULL,
    PageCount INTEGER NOT NULL,
    ImportedUtc TEXT NOT NULL,
    CONSTRAINT CK_LibraryDocuments_ByteLength CHECK (ByteLength > 0),
    CONSTRAINT CK_LibraryDocuments_PageCount CHECK (PageCount > 0)
);
CREATE UNIQUE INDEX IX_LibraryDocuments_StorageKey ON LibraryDocuments (StorageKey);
CREATE INDEX IX_LibraryDocuments_Sha256 ON LibraryDocuments (Sha256);
CREATE TABLE ProjectDocuments (
    ProjectId TEXT NOT NULL,
    DocumentId TEXT NOT NULL,
    Role TEXT NOT NULL,
    AddedUtc TEXT NOT NULL,
    PRIMARY KEY (ProjectId, DocumentId),
    CONSTRAINT CK_ProjectDocuments_Role CHECK (Role IN ('FocusedSource', 'ReferenceSource')),
    FOREIGN KEY (ProjectId) REFERENCES ResearchProjects (Id) ON DELETE RESTRICT,
    FOREIGN KEY (DocumentId) REFERENCES LibraryDocuments (Id) ON DELETE RESTRICT
);
CREATE INDEX IX_ProjectDocuments_DocumentId ON ProjectDocuments (DocumentId);
        """) |> ignore
    override _.Down(migration: MigrationBuilder) =
        migration.Sql("DROP TABLE ProjectDocuments; DROP TABLE LibraryDocuments;") |> ignore
    override _.BuildTargetModel(model: ModelBuilder) =
        model.HasAnnotation("ProductVersion", "10.0.14") |> ignore
        let workspace = model.Entity("LexScholar.Infrastructure.WorkspaceRow")
        workspace.ToTable("Workspaces") |> ignore
        workspace.Property<System.Guid>("Id").ValueGeneratedNever().HasConversion<CanonicalGuidConverter>().HasColumnType("TEXT") |> ignore
        workspace.Property<System.Nullable<System.Guid>>("ActiveProjectId").HasConversion<CanonicalGuidConverter>().HasColumnType("TEXT") |> ignore
        workspace.Property<System.DateTimeOffset>("CreatedUtc").HasConversion<UtcDateTimeOffsetConverter>().HasColumnType("TEXT") |> ignore
        workspace.HasKey("Id") |> ignore
        workspace.HasIndex([| "Id"; "ActiveProjectId" |]) |> ignore
        let project = model.Entity("LexScholar.Infrastructure.ResearchProjectRow")
        project.ToTable("ResearchProjects") |> ignore
        project.Property<System.Guid>("Id").ValueGeneratedNever().HasConversion<CanonicalGuidConverter>().HasColumnType("TEXT") |> ignore
        project.Property<System.Guid>("WorkspaceId").HasConversion<CanonicalGuidConverter>().HasColumnType("TEXT") |> ignore
        project.Property<string>("Name").IsRequired().HasColumnType("TEXT") |> ignore
        project.Property<string>("NormalizedName").IsRequired().HasColumnType("TEXT") |> ignore
        project.Property<System.DateTimeOffset>("CreatedUtc").HasConversion<UtcDateTimeOffsetConverter>().HasColumnType("TEXT") |> ignore
        project.HasKey("Id") |> ignore
        project.HasAlternateKey("WorkspaceId", "Id") |> ignore
        project.HasIndex([| "WorkspaceId"; "NormalizedName" |]).IsUnique() |> ignore
        project.HasOne("LexScholar.Infrastructure.WorkspaceRow", null).WithMany().HasForeignKey("WorkspaceId").OnDelete(DeleteBehavior.Restrict) |> ignore
        workspace.HasOne("LexScholar.Infrastructure.ResearchProjectRow", null).WithMany().HasForeignKey("Id", "ActiveProjectId").HasPrincipalKey("WorkspaceId", "Id").OnDelete(DeleteBehavior.Restrict) |> ignore
        let document = model.Entity("LexScholar.Infrastructure.LibraryDocumentRow")
        document.ToTable("LibraryDocuments", fun (table: TableBuilder) ->
            table.HasCheckConstraint("CK_LibraryDocuments_ByteLength", "ByteLength > 0") |> ignore
            table.HasCheckConstraint("CK_LibraryDocuments_PageCount", "PageCount > 0") |> ignore) |> ignore
        document.Property<System.Guid>("Id").ValueGeneratedNever().HasConversion<CanonicalGuidConverter>().HasColumnType("TEXT") |> ignore
        document.Property<string>("Sha256").IsRequired().HasColumnType("TEXT") |> ignore
        document.Property<string>("StorageKey").IsRequired().HasColumnType("TEXT") |> ignore
        document.Property<string>("OriginalFileName").IsRequired().HasColumnType("TEXT") |> ignore
        document.Property<int64>("ByteLength").HasColumnType("INTEGER") |> ignore
        document.Property<int>("PageCount").HasColumnType("INTEGER") |> ignore
        document.Property<System.DateTimeOffset>("ImportedUtc").HasConversion<UtcDateTimeOffsetConverter>().HasColumnType("TEXT") |> ignore
        document.HasKey("Id") |> ignore
        document.HasIndex([| "Sha256" |]) |> ignore
        document.HasIndex([| "StorageKey" |]).IsUnique() |> ignore
        let association = model.Entity("LexScholar.Infrastructure.ProjectDocumentRow")
        association.ToTable("ProjectDocuments", fun (table: TableBuilder) ->
            table.HasCheckConstraint("CK_ProjectDocuments_Role", "Role IN ('FocusedSource', 'ReferenceSource')") |> ignore) |> ignore
        association.Property<System.Guid>("ProjectId").HasConversion<CanonicalGuidConverter>().HasColumnType("TEXT") |> ignore
        association.Property<System.Guid>("DocumentId").HasConversion<CanonicalGuidConverter>().HasColumnType("TEXT") |> ignore
        association.Property<string>("Role").IsRequired().HasColumnType("TEXT") |> ignore
        association.Property<System.DateTimeOffset>("AddedUtc").HasConversion<UtcDateTimeOffsetConverter>().HasColumnType("TEXT") |> ignore
        association.HasKey("ProjectId", "DocumentId") |> ignore
        association.HasIndex([| "DocumentId" |]) |> ignore
        association.HasOne("LexScholar.Infrastructure.ResearchProjectRow", null).WithMany().HasForeignKey("ProjectId").OnDelete(DeleteBehavior.Restrict) |> ignore
        association.HasOne("LexScholar.Infrastructure.LibraryDocumentRow", null).WithMany().HasForeignKey("DocumentId").OnDelete(DeleteBehavior.Restrict) |> ignore

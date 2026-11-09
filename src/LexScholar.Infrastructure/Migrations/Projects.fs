namespace LexScholar.Infrastructure.Migrations

open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Infrastructure
open Microsoft.EntityFrameworkCore.Migrations
open LexScholar.Infrastructure

[<DbContext(typeof<WorkspaceDb>); Migration("20261110000000_Projects")>]
type Projects() =
    inherit Migration()
    override _.Up(migration: MigrationBuilder) =
        migration.Sql("""
CREATE TABLE Workspaces (
    Id TEXT NOT NULL PRIMARY KEY,
    ActiveProjectId TEXT NULL,
    CreatedUtc TEXT NOT NULL,
    FOREIGN KEY (Id, ActiveProjectId) REFERENCES ResearchProjects (WorkspaceId, Id) ON DELETE RESTRICT
);
CREATE TABLE ResearchProjects (
    Id TEXT NOT NULL PRIMARY KEY,
    WorkspaceId TEXT NOT NULL,
    Name TEXT NOT NULL,
    NormalizedName TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    UNIQUE (WorkspaceId, Id),
    FOREIGN KEY (WorkspaceId) REFERENCES Workspaces (Id) ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IX_ResearchProjects_WorkspaceId_NormalizedName ON ResearchProjects (WorkspaceId, NormalizedName);
CREATE INDEX IX_Workspaces_Id_ActiveProjectId ON Workspaces (Id, ActiveProjectId);
        """) |> ignore
    override _.Down(migration: MigrationBuilder) =
        // Break the optional cycle before dropping the project schema.
        migration.Sql("UPDATE Workspaces SET ActiveProjectId = NULL; DROP TABLE ResearchProjects; DROP TABLE Workspaces;") |> ignore
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

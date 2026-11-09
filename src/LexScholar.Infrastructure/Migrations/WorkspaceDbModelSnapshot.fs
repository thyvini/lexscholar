namespace LexScholar.Infrastructure.Migrations

open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Infrastructure
open LexScholar.Infrastructure

[<DbContext(typeof<WorkspaceDb>)>]
type WorkspaceDbModelSnapshot() =
    inherit ModelSnapshot()
    override _.BuildModel(model: ModelBuilder) =
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

namespace LexScholar.Infrastructure

open System
open System.Globalization
open System.IO
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Storage.ValueConversion
open Microsoft.FSharp.Linq.RuntimeHelpers

module private UtcTimestamp =
    let write (value: DateTime) =
        let utc =
            match value.Kind with
            | DateTimeKind.Utc -> value
            | DateTimeKind.Local -> value.ToUniversalTime()
            | _ -> invalidArg "value" "A timestamp must have an explicit UTC or local kind."
        utc.ToString("O", CultureInfo.InvariantCulture)

    let read (value: string) =
        let parsed = DateTime.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        if parsed.Kind <> DateTimeKind.Utc then
            raise (InvalidDataException("A stored timestamp must be UTC."))
        parsed

type CanonicalGuidConverter() =
    inherit ValueConverter<Guid, string>(
        LeafExpressionConverter.QuotationToLambdaExpression <@ Func<Guid, string>(fun value -> value.ToString("D")) @>,
        LeafExpressionConverter.QuotationToLambdaExpression <@ Func<string, Guid>(fun value -> Guid.Parse(value)) @>)

type UtcDateTimeConverter() =
    inherit ValueConverter<DateTime, string>(
        LeafExpressionConverter.QuotationToLambdaExpression <@ Func<DateTime, string>(fun value -> UtcTimestamp.write value) @>,
        LeafExpressionConverter.QuotationToLambdaExpression <@ Func<string, DateTime>(fun value -> UtcTimestamp.read value) @>)

type UtcDateTimeOffsetConverter() =
    inherit ValueConverter<DateTimeOffset, string>(
        LeafExpressionConverter.QuotationToLambdaExpression <@ Func<DateTimeOffset, string>(fun value -> UtcTimestamp.write value.UtcDateTime) @>,
        LeafExpressionConverter.QuotationToLambdaExpression <@ Func<string, DateTimeOffset>(fun value -> DateTimeOffset(UtcTimestamp.read value)) @>)

// EF persistence shapes only; the immutable domain records remain independent.
type WorkspaceRow() =
    member val Id = Guid.Empty with get, set
    member val ActiveProjectId = Nullable<Guid>() with get, set
    member val CreatedUtc = DateTimeOffset.MinValue with get, set

type ResearchProjectRow() =
    member val Id = Guid.Empty with get, set
    member val WorkspaceId = Guid.Empty with get, set
    member val Name = "" with get, set
    member val NormalizedName = "" with get, set
    member val CreatedUtc = DateTimeOffset.MinValue with get, set

type WorkspaceDb(options: DbContextOptions) =
    inherit DbContext(options)
    override _.ConfigureConventions(builder: ModelConfigurationBuilder) =
        builder.Properties<Guid>().HaveConversion<CanonicalGuidConverter>().HaveColumnType("TEXT") |> ignore
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>().HaveColumnType("TEXT") |> ignore
        builder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>().HaveColumnType("TEXT") |> ignore
    override _.OnModelCreating(model: ModelBuilder) =
        let workspace = model.Entity<WorkspaceRow>()
        workspace.ToTable("Workspaces") |> ignore
        workspace.HasKey("Id") |> ignore
        workspace.Property<Guid>("Id").ValueGeneratedNever() |> ignore
        let project = model.Entity<ResearchProjectRow>()
        project.ToTable("ResearchProjects") |> ignore
        project.HasKey("Id") |> ignore
        project.Property<Guid>("Id").ValueGeneratedNever() |> ignore
        project.Property<string>("Name").IsRequired() |> ignore
        project.Property<string>("NormalizedName").IsRequired() |> ignore
        project.HasAlternateKey("WorkspaceId", "Id") |> ignore
        project.HasIndex([| "WorkspaceId"; "NormalizedName" |]).IsUnique() |> ignore
        project.HasOne<WorkspaceRow>().WithMany().HasForeignKey("WorkspaceId").OnDelete(DeleteBehavior.Restrict) |> ignore
        workspace.HasOne<ResearchProjectRow>().WithMany().HasForeignKey("Id", "ActiveProjectId").HasPrincipalKey("WorkspaceId", "Id").OnDelete(DeleteBehavior.Restrict) |> ignore

module WorkspaceDb =
    let databasePath dataDirectory = Path.Combine(dataDirectory, "workspace.sqlite")

    let connectionString dataDirectory =
        // Disable pooling so startup and failures release SQLite handles promptly.
        SqliteConnectionStringBuilder(DataSource = databasePath dataDirectory, ForeignKeys = Nullable(true), Pooling = false).ToString()

    let create dataDirectory =
        let options = DbContextOptionsBuilder().UseSqlite(connectionString dataDirectory).Options
        new WorkspaceDb(options)

    /// The caller must hold the data-directory lock throughout initialization.
    let initialize dataDirectory =
        use db = create dataDirectory
        db.Database.Migrate()

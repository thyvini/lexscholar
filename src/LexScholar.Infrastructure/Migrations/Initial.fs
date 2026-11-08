namespace LexScholar.Infrastructure.Migrations

open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Infrastructure
open Microsoft.EntityFrameworkCore.Migrations
open LexScholar.Infrastructure

// The baseline intentionally creates no domain tables; later tasks own them.
[<DbContext(typeof<WorkspaceDb>); Migration("20261108000000_Initial")>]
type Initial() =
    inherit Migration()
    override _.Up(_migration: MigrationBuilder) = ()
    override _.Down(_migration: MigrationBuilder) = ()
    override _.BuildTargetModel(model: ModelBuilder) =
        model.HasAnnotation("ProductVersion", "10.0.14") |> ignore

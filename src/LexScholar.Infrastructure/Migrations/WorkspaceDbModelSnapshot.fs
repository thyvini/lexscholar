namespace LexScholar.Infrastructure.Migrations

open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Infrastructure
open LexScholar.Infrastructure

[<DbContext(typeof<WorkspaceDb>)>]
type WorkspaceDbModelSnapshot() =
    inherit ModelSnapshot()
    override _.BuildModel(model: ModelBuilder) =
        model.HasAnnotation("ProductVersion", "10.0.14") |> ignore

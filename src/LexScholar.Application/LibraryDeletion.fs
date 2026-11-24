/// Non-destructive deletion impact and revision comparison input.
module LexScholar.Application.LibraryDeletion

open System
open System.Security.Cryptography
open System.Text.Json

type ProjectSource =
    { Id: Guid
      Name: string
      Role: string
      Summary: (string * DateTimeOffset) option }

type DocumentSource =
    { Id: Guid
      Name: string
      Projects: ProjectSource list }

type ProjectImpact =
    { Id: Guid
      Name: string
      Role: string
      HasSummary: bool }

type DocumentImpact =
    { Id: Guid
      Name: string
      Projects: ProjectImpact list }

type Preview =
    { Documents: DocumentImpact list
      Revision: string }

[<RequireQualifiedAccess>]
type PreviewError =
    | InvalidSelection
    | DocumentNotFound

/// Reads must capture all selected impact, including raw stored summary identity,
/// in one snapshot. Summary content is hashed only; it never enters the response.
let preview (read: Guid list -> DocumentSource list) (ids: Guid list) =
    if ids.IsEmpty || ids |> List.contains Guid.Empty || (Set.ofList ids).Count <> ids.Length then
        Error PreviewError.InvalidSelection
    else
        let documents = read (List.sort ids) |> List.sortBy _.Id
        if (documents |> List.map _.Id) <> List.sort ids then Error PreviewError.DocumentNotFound
        else
            let canonical =
                documents |> List.map (fun document ->
                    {| id = document.Id; name = document.Name
                       projects =
                        document.Projects |> List.sortBy _.Id |> List.map (fun project ->
                            {| id = project.Id; name = project.Name; role = project.Role
                               summary = project.Summary |> Option.map fst |> Option.defaultValue null
                               generatedAt = project.Summary |> Option.map (fun (_, time) -> time.ToUniversalTime().ToString("O")) |> Option.defaultValue null |}) |})
            let revision = canonical |> JsonSerializer.SerializeToUtf8Bytes |> SHA256.HashData |> Convert.ToHexString
            Ok
                { Revision = revision
                  Documents =
                    documents |> List.map (fun document ->
                        { Id = document.Id; Name = document.Name
                          Projects = document.Projects |> List.sortBy _.Id |> List.map (fun project ->
                              { Id = project.Id; Name = project.Name; Role = project.Role; HasSummary = project.Summary.IsSome }) }) }

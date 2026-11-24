/// Deletion impact and durable confirmation admission; no destructive processing.
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

let validSelection (ids: Guid list) =
    not ids.IsEmpty && not (ids |> List.contains Guid.Empty) && (Set.ofList ids).Count = ids.Length

/// Reads must capture all selected impact, including raw stored summary identity,
/// in one snapshot. Summary content is hashed only; it never enters the response.
let preview (read: Guid list -> DocumentSource list) (ids: Guid list) =
    if not (validSelection ids) then
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

[<RequireQualifiedAccess>]
type AdmissionError =
    | InvalidSelection
    | DocumentNotFound
    | OperationConflict
    | DeletionInProgress
    | ImpactChanged of Preview

type AdmissionStore =
    { Existing: Guid -> Guid list option
      Read: Guid list -> DocumentSource list
      Pending: Guid list -> bool
      Persist: Guid -> Guid list -> unit }

/// Caller holds the workspace mutation boundary and one write transaction.
/// An existing operation's selection is authoritative even after impact changes
/// or subsequent processing removes its document rows.
let admit (store: AdmissionStore) operation ids revision =
    if operation = Guid.Empty || not (validSelection ids) || String.IsNullOrWhiteSpace revision then
        Error AdmissionError.InvalidSelection
    else
        let selected = List.sort ids
        match store.Existing operation with
        | Some existing when List.sort existing = selected -> Ok operation
        | Some _ -> Error AdmissionError.OperationConflict
        | None ->
            match preview store.Read selected with
            | Error PreviewError.InvalidSelection -> Error AdmissionError.InvalidSelection
            | Error PreviewError.DocumentNotFound -> Error AdmissionError.DocumentNotFound
            | Ok fresh when fresh.Revision <> revision -> Error (AdmissionError.ImpactChanged fresh)
            | Ok _ when store.Pending selected -> Error AdmissionError.DeletionInProgress
            | Ok _ ->
                store.Persist operation selected
                Ok operation

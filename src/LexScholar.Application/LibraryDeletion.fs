/// Deletion impact, durable confirmation and internal per-item processing.
module LexScholar.Application.LibraryDeletion

open System
open System.Security.Cryptography
open System.Text.Json
open System.Threading.Tasks
open System.Collections.Generic

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

/// Workspace -> registry -> SQLite. Intent commits before cancellation, while
/// completion/admission remain excluded. A refused confirmation fences nothing.
/// The production host does not compose deletion admission until T017.
let admitFenced (jobs: SummaryJobs) persist operation ids revision =
    jobs.Locked(fun () ->
        let result = persist operation ids revision
        match result with
        | Ok _ -> jobs.FenceDocuments ids
        | Error _ -> ()
        result)

[<RequireQualifiedAccess>]
type MetadataError =
    | ItemNotFound
    | StorageError

/// Reuse the admission lock order and fence again before processing an item.
/// Not composed by the production host before T017.
let removeMetadata (jobs: SummaryJobs) hasIntent remove operation document =
    jobs.Locked(fun () ->
        // Intent lookup opens storage too: a failed read is neither missing
        // authority nor permission to fence/delete. Leave the journal intact
        // and return a per-item failure so processing can continue elsewhere.
        let intent =
            try Ok (hasIntent operation document)
            with _ -> Error MetadataError.StorageError
        match intent with
        | Error error -> Error error
        | Ok false -> Error MetadataError.ItemNotFound
        | Ok true ->
            jobs.FenceDocuments [ document ]
            remove operation document)

/// Internal per-document lifecycle. Acquire files BEFORE mutation/registry, and
/// release the registry before unlink/fsync. Not wired into production until T017.
let processItem (coordination: LibraryCoordination) (jobs: SummaryJobs) hasIntent remove finish operation document cancellation = task {
    use! _files = coordination.FileLease cancellation
    match removeMetadata jobs hasIntent remove operation document with
    | Error error -> return Error error
    | Ok _ -> return finish operation document }

type ItemResult =
    { DocumentId: Guid
      Name: string
      Outcome: string
      Reason: string }

type OperationResult =
    { OperationId: Guid
      Status: string
      Items: ItemResult list }

/// One processor per host, never tied to a request's cancellation. The journal is
/// authoritative; only currently executing work is process-local. All reads,
/// starts and acknowledgement share the workspace boundary.
type Operations(coordination: LibraryCoordination,
                read: unit -> OperationResult list,
                acknowledge: Guid -> Result<unit, string>,
                runItem: Guid -> Guid -> Task<unit>) =
    let active = Dictionary<Guid, TaskCompletionSource>()
    let snapshot () =
        read () |> List.map (fun result ->
            let status =
                if not result.Items.IsEmpty && result.Items |> List.forall (fun item -> item.Outcome = "deleted") then "complete"
                elif active.ContainsKey result.OperationId then "running"
                else "partial"
            { result with Status = status })

    member _.List() = coordination.Mutation snapshot
    member _.Find operation =
        coordination.Mutation(fun () -> snapshot () |> List.tryFind (fun result -> result.OperationId = operation))

    member _.Start operation =
        coordination.Mutation(fun () ->
            match snapshot () |> List.tryFind (fun result -> result.OperationId = operation) with
            | None -> false
            | Some result ->
                if result.Status <> "complete" && not (active.ContainsKey operation) then
                    let completion = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
                    active.Add(operation, completion)
                    Task.Run(Func<Task>(fun () -> task {
                        try
                            for item in result.Items do
                                if item.Outcome <> "deleted" then
                                    // Failure of one item must not skip the others. Durable
                                    // adapters record sanitized failures where storage allows.
                                    try do! runItem operation item.DocumentId
                                    with _ -> ()
                        finally
                            coordination.Mutation(fun () ->
                                active.Remove operation |> ignore
                                completion.SetResult()) })) |> ignore
                true)

    member _.Settled operation =
        coordination.Mutation(fun () ->
            match active.TryGetValue operation with
            | true, completion -> completion.Task
            | _ -> Task.CompletedTask)

    member _.Acknowledge operation =
        coordination.Mutation(fun () ->
            if active.ContainsKey operation then Error "deletion_in_progress"
            else acknowledge operation)

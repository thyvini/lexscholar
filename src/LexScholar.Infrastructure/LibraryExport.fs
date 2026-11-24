/// Private, streamed ZIP construction from one captured database state.
module LexScholar.Infrastructure.LibraryExport

open System
open System.IO
open System.IO.Compression
open System.Globalization
open System.Text
open System.Text.Json
open System.Threading
open LexScholar.Application.LibraryExport
open LexScholar.Domain

type private ExportFailure = LexScholar.Application.LibraryExport.Failure

let private pdfPath (id: Guid) = $"pdf/{id:D}.pdf"
let private textPath (key: SummaryKey) = $"summaries/{key.Project:D}/{key.Document:D}.txt"
let private reviewNotice = "Gerado para sua revisão. Não verificado."

let private summaryManifest key (summary: StoredSummary) =
    {| generated = true
       reviewNotice = reviewNotice
       generatedAt = summary.GeneratedAt.UtcDateTime
       serviceHost = summary.ServiceHost
       model = summary.Model
       pagesCovered = summary.PagesCovered
       unreadablePages = summary.UnreadablePages
       omittedPoints = summary.OmittedPoints
       points = summary.Points |> List.map (fun point -> {| text = point.Text; pages = point.Pages |})
       textPath = textPath key |}

let private summaryText (snapshot: LibraryExportSnapshot.Snapshot) (key: SummaryKey) (summary: StoredSummary) =
    let document = snapshot.Documents |> List.find (fun row -> row.Id = key.Document)
    let project = snapshot.Projects |> List.find (fun row -> row.Id = key.Project)
    let pages values = values |> List.map (fun (page: int) -> page.ToString(CultureInfo.InvariantCulture)) |> String.concat ", "
    let generatedAt = summary.GeneratedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
    [ reviewNotice
      $"Documento: {document.OriginalFileName}"
      $"ID do documento: {key.Document:D}"
      $"Projeto: {project.Name}"
      $"ID do projeto: {key.Project:D}"
      $"PDF de origem: {pdfPath key.Document}"
      $"Gerado em: {generatedAt}"
      $"Serviço: {summary.ServiceHost}"
      $"Modelo: {summary.Model}"
      $"Páginas cobertas: {pages summary.PagesCovered}"
      $"Páginas ilegíveis: {pages summary.UnreadablePages}"
      $"Pontos omitidos: {summary.OmittedPoints.ToString(CultureInfo.InvariantCulture)}"
      ""
      for point in summary.Points do
          point.Text
          $"Páginas: {pages point.Pages}"
          "" ]
    |> String.concat "\n"

let private writeText (archive: ZipArchive) path text =
    use stream = archive.CreateEntry(path).Open()
    use writer = new StreamWriter(stream, UTF8Encoding(false))
    writer.Write(text: string)

let private manifest (snapshot: LibraryExportSnapshot.Snapshot) =
    let summaries = snapshot.Summaries |> Map.ofList
    {| format = "lexscholar-library"
       version = 1
       documents =
        snapshot.Documents |> List.map (fun row ->
            {| id = row.Id.ToString("D")
               originalFileName = row.OriginalFileName
               byteLength = row.ByteLength
               pageCount = row.PageCount
               importedUtc = row.ImportedUtc.UtcDateTime
               sha256 = row.Sha256
               pdfPath = pdfPath row.Id |})
       projects =
        snapshot.Projects |> List.map (fun row ->
            {| id = row.Id.ToString("D"); name = row.Name; createdUtc = row.CreatedUtc.UtcDateTime |})
       associations =
        snapshot.Associations |> List.map (fun row ->
            {| projectId = row.ProjectId.ToString("D")
               documentId = row.DocumentId.ToString("D")
               role = row.Role
               addedUtc = row.AddedUtc.UtcDateTime
               summary =
                summaries
                |> Map.tryFind { Project = row.ProjectId; Document = row.DocumentId }
                |> Option.map (summaryManifest { Project = row.ProjectId; Document = row.DocumentId } >> box)
                |> Option.defaultValue null |}) |}

/// Narrow filesystem seams for deterministic read/write failure tests.
type ExportFiles =
    { OpenOriginal: string -> Stream
      CreateArchive: string -> Stream }

exception private DocumentReadFailure of Guid * string

let private createArchive path : Stream =
    let options = FileStreamOptions()
    options.Mode <- FileMode.CreateNew
    options.Access <- FileAccess.ReadWrite
    options.Share <- FileShare.None
    options.Options <- FileOptions.DeleteOnClose
    options.UnixCreateMode <- Nullable(UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
    new FileStream(path, options)

let buildWith (files: ExportFiles) directory : Build = fun cancellationToken ->
    let mutable output: Stream = null
    try
        cancellationToken.ThrowIfCancellationRequested()
        let snapshot = LibraryExportSnapshot.capture directory
        if snapshot.Documents.IsEmpty then
            Error ExportFailure.EmptyLibrary
        else
            let staging = DocumentStore.exportDirectory directory
            Directory.CreateDirectory(staging, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
            output <- files.CreateArchive(Path.Combine(staging, $"{Guid.NewGuid():N}.zip"))
            do
                use archive = new ZipArchive(output, ZipArchiveMode.Create, true, Encoding.UTF8)
                writeText archive "README.txt"
                    "Biblioteca LexScholar\nPDFs originais e metadados em manifest.json.\nResumos: Gerado para sua revisão. Não verificado.\nAs páginas citadas são posições no PDF, contadas a partir de 1.\nEste arquivo não oferece restauração no LexScholar.\n"
                writeText archive "manifest.json" (JsonSerializer.Serialize(manifest snapshot))
                for key, summary in snapshot.Summaries do
                    cancellationToken.ThrowIfCancellationRequested()
                    writeText archive (textPath key) (summaryText snapshot key summary)
                for document in snapshot.Documents do
                    cancellationToken.ThrowIfCancellationRequested()
                    let unavailable () = raise (DocumentReadFailure(document.Id, document.OriginalFileName))
                    let read action =
                        try action () with
                        | :? OperationCanceledException -> reraise()
                        | _ -> unavailable ()
                    use source = read (fun () -> files.OpenOriginal document.StorageKey)
                    if read (fun () -> source.Length) <> document.ByteLength then unavailable ()
                    use target = archive.CreateEntry(pdfPath document.Id).Open()
                    // Never buffer a PDF or the archive in memory. Check every
                    // read, including EOF, so a file changed during copying
                    // cannot silently produce a short or overlong original.
                    let buffer = Array.zeroCreate<byte> 81_920
                    let mutable total = 0L
                    let mutable finished = false
                    while not finished do
                        cancellationToken.ThrowIfCancellationRequested()
                        let count = read (fun () -> source.Read(buffer, 0, buffer.Length))
                        total <- total + int64 count
                        if total > document.ByteLength then unavailable ()
                        if count = 0 then finished <- true
                        else target.Write(buffer, 0, count)
                    if total <> document.ByteLength then unavailable ()
            output.Flush()
            output.Position <- 0L
            Ok output
    with
    | :? OperationCanceledException ->
        if not (isNull output) then output.Dispose()
        reraise()
    | DocumentReadFailure (id, name) ->
        if not (isNull output) then output.Dispose()
        Error (ExportFailure.DocumentUnavailable(id, name))
    | _ ->
        if not (isNull output) then output.Dispose()
        Error ExportFailure.ExportFailed

let build directory =
    buildWith
        { OpenOriginal = fun key ->
              DocumentStore.openOriginal directory key CancellationToken.None
              |> fun pending -> pending.GetAwaiter().GetResult()
          CreateArchive = createArchive }
        directory

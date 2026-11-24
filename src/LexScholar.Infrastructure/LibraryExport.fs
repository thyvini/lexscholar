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

let build directory : Build = fun cancellationToken ->
    let mutable output: FileStream = null
    let mutable failure = ExportFailure.ExportFailed
    try
        cancellationToken.ThrowIfCancellationRequested()
        let snapshot = LibraryExportSnapshot.capture directory
        if snapshot.Documents.IsEmpty then
            Error ExportFailure.EmptyLibrary
        else
            let staging = Path.Combine(directory, "exports")
            Directory.CreateDirectory(staging, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
            let options = FileStreamOptions()
            options.Mode <- FileMode.CreateNew
            options.Access <- FileAccess.ReadWrite
            options.Share <- FileShare.None
            options.Options <- FileOptions.DeleteOnClose
            options.UnixCreateMode <- Nullable(UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            output <- new FileStream(Path.Combine(staging, $"{Guid.NewGuid():N}.zip"), options)
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
                    failure <- ExportFailure.DocumentUnavailable(document.Id, document.OriginalFileName)
                    use source = new FileStream(Path.Combine(DocumentStore.originalsDirectory directory, document.StorageKey), FileMode.Open, FileAccess.Read, FileShare.Read)
                    if source.Length <> document.ByteLength then
                        raise (InvalidDataException("Original length differs."))
                    use target = archive.CreateEntry(pdfPath document.Id).Open()
                    source.CopyTo(target)
                    failure <- ExportFailure.ExportFailed
            output.Flush()
            output.Position <- 0L
            Ok (output :> Stream)
    with
    | :? OperationCanceledException ->
        if not (isNull output) then output.Dispose()
        reraise()
    | _ ->
        if not (isNull output) then output.Dispose()
        Error failure

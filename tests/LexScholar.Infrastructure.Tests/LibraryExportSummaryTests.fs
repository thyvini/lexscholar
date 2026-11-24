module LexScholar.Infrastructure.Tests.LibraryExportSummaryTests

open System
open System.IO
open System.IO.Compression
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open LexScholar.Application
open LexScholar.Domain
open LexScholar.Infrastructure
open LexScholar.Infrastructure.Tests.SchemaTestSupport
open Xunit

let private notice = "Gerado para sua revisão. Não verificado."
let private time = DateTimeOffset.Parse("2026-11-12T12:34:56Z")

let private setup directory =
    WorkspaceDb.initialize directory
    use c = openConnection directory
    let workspace, first, second, document = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    addWorkspace c workspace
    addProject c first workspace "Projeto Um"
    addProject c second workspace "Projeto Dois"
    Directory.CreateDirectory(DocumentStore.originalsDirectory directory) |> ignore
    File.WriteAllBytes(Path.Combine(DocumentStore.originalsDirectory directory, $"{document:N}"), [| 1uy; 2uy |])
    execute c $"INSERT INTO LibraryDocuments VALUES ('{document:D}', 'sha', '{document:N}', 'fonte ação.pdf', 2, 8, '2026-11-12T12:34:56.0000000Z')"
    for project in [first; second] do
        execute c $"INSERT INTO ProjectDocuments VALUES ('{project:D}', '{document:D}', 'FocusedSource', '2026-11-12T12:34:56.0000000Z')"
    let summary text =
        { DocumentId = document; Points = [{ Text = text; Pages = [1; 3; 8] }; { Text = "Outro ponto"; Pages = [2] }]
          PagesCovered = [1; 2; 3; 8]; UnreadablePages = [4; 7]; OmittedPoints = 5
          GeneratedAt = time; ServiceHost = "ChatGPT plan"; Model = "modelo-á" }
    let keys = [ { Project = first; Document = document }, summary "Primeiro ponto"
                 { Project = second; Document = document }, summary "Ponto separado" ]
    for key, saved in keys do SummaryRepository.save directory key saved
    keys

let private build directory =
    match LibraryExport.build directory CancellationToken.None with
    | Ok stream -> stream
    | Error failure -> failwithf "Expected complete summary export, got %A" failure

let private read (zip: ZipArchive) path =
    let entry = zip.GetEntry path
    Assert.NotNull entry
    use reader = new StreamReader(entry.Open())
    reader.ReadToEnd()

let private ints (value: JsonElement) = value.EnumerateArray() |> Seq.map _.GetInt32() |> Seq.toList

[<Fact; Trait("Task", "T005")>]
let ``archive preserves every structured and readable summary field per project`` () =
    use dir = new TemporaryDirectory()
    let keys = setup dir.Path
    use stream = build dir.Path
    use zip = new ZipArchive(stream, ZipArchiveMode.Read)
    use manifest = JsonDocument.Parse(read zip "manifest.json")
    let associations = manifest.RootElement.GetProperty("associations").EnumerateArray() |> Seq.toList
    Assert.Equal(2, associations.Length)
    Assert.Equal(2, zip.Entries |> Seq.filter (fun entry -> entry.FullName.StartsWith("summaries/")) |> Seq.length)
    for key, saved in keys do
        let association = associations |> List.find (fun row -> row.GetProperty("projectId").GetGuid() = key.Project)
        Assert.Equal(key.Document, association.GetProperty("documentId").GetGuid())
        let summary = association.GetProperty("summary")
        Assert.True(summary.GetProperty("generated").GetBoolean())
        Assert.Equal(notice, summary.GetProperty("reviewNotice").GetString())
        Assert.Equal(saved.GeneratedAt, summary.GetProperty("generatedAt").GetDateTimeOffset())
        Assert.Equal(saved.ServiceHost, summary.GetProperty("serviceHost").GetString())
        Assert.Equal(saved.Model, summary.GetProperty("model").GetString())
        Assert.Equal<int list>(saved.PagesCovered, ints (summary.GetProperty("pagesCovered")))
        Assert.Equal<int list>(saved.UnreadablePages, ints (summary.GetProperty("unreadablePages")))
        Assert.Equal(saved.OmittedPoints, summary.GetProperty("omittedPoints").GetInt32())
        let points = summary.GetProperty("points").EnumerateArray() |> Seq.toList
        Assert.Equal(saved.Points.Length, points.Length)
        for actual, expected in List.zip points saved.Points do
            Assert.Equal(expected.Text, actual.GetProperty("text").GetString())
            Assert.Equal<int list>(expected.Pages, ints (actual.GetProperty("pages")))
        let path = $"summaries/{key.Project:D}/{key.Document:D}.txt"
        Assert.Equal(path, summary.GetProperty("textPath").GetString())
        let text = read zip path
        for expected in [ notice; "fonte ação.pdf"; string key.Document; string key.Project
                          (if key = fst keys.Head then "Projeto Um" else "Projeto Dois")
                          $"pdf/{key.Document:D}.pdf"; saved.ServiceHost; saved.Model
                          "2026-11-12T12:34:56"; "Páginas cobertas: 1, 2, 3, 8"
                          "Páginas ilegíveis: 4, 7"; "Pontos omitidos: 5"
                          saved.Points.Head.Text
                          "Páginas: 1, 3, 8"; "Outro ponto"; "Páginas: 2" ] do
            Assert.Contains(expected, text)
        let other = keys |> List.find (fun (other, _) -> other <> key) |> snd
        Assert.DoesNotContain(other.Points.Head.Text, text)
    let readme = read zip "README.txt"
    Assert.Contains(notice, readme)
    Assert.Contains("contadas a partir de 1", readme)
    Assert.Contains("não oferece restauração", readme)

[<Fact; Trait("Task", "T005")>]
let ``export during regeneration keeps the durable old version without changing the job`` () = task {
    use dir = new TemporaryDirectory()
    let keys = setup dir.Path
    let key, saved = keys.Head
    let jobs = SummaryJobs({ Find = SummaryRepository.find dir.Path; Save = SummaryRepository.save dir.Path
                             Delete = SummaryRepository.delete dir.Path; AssociationExists = fun _ -> true })
    let entered = TaskCompletionSource<JobHandle>(TaskCreationOptions.RunContinuationsAsynchronously)
    let release = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let run handle : Task<unit> = task { entered.SetResult handle; do! release.Task }
    Assert.Equal(Started, jobs.Start(key, CancellationToken.None, run, regenerate = true))
    let! handle = entered.Task
    try
        use stream = build dir.Path
        use zip = new ZipArchive(stream, ZipArchiveMode.Read)
        let text = read zip $"summaries/{key.Project:D}/{key.Document:D}.txt"
        Assert.Contains(saved.Points.Head.Text, text)
        Assert.Equal(Some (JobState.Running None), jobs.State(key, includeReplacement = true))
        Assert.False(handle.Token.IsCancellationRequested)
        Assert.Equal(Some saved, SummaryRepository.find dir.Path key)
    finally
        release.TrySetResult() |> ignore
    do! jobs.Settled key
}

[<Fact; Trait("Task", "T005")>]
let ``unknown stored version refuses the whole export instead of omitting a summary`` () =
    use dir = new TemporaryDirectory()
    setup dir.Path |> ignore
    use c = openConnection dir.Path
    execute c "UPDATE DocumentSummaries SET Content = '{\"version\":999}'"
    Assert.Equal(Error LexScholar.Application.LibraryExport.Failure.ExportFailed, LibraryExport.build dir.Path CancellationToken.None)

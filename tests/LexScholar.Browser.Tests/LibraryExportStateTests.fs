module LexScholar.Browser.Tests.LibraryExportStateTests

open Xunit
open LexScholar.Client

[<Fact; Trait("Task", "T008")>]
let ``export is guarded independently and navigation does not forget an in flight download`` () =
    let started, effect = Library.update Library.Export Library.initial
    Assert.Equal(Some Library.SendExport, effect)
    Assert.Equal(Library.Exporting, started.ExportState)
    let repeated, duplicate = Library.update Library.Export started
    Assert.Equal(started, repeated)
    Assert.True(duplicate.IsNone)
    let navigated, _ = Library.update (Library.ActiveProjectChanged (Some (System.Guid.NewGuid()))) started
    Assert.Equal(Library.Exporting, navigated.ExportState)
    let received, _ = Library.update (Library.Exported (Ok ())) navigated
    Assert.Equal(Library.ExportReceived, received.ExportState)
    let retry, effect = Library.update Library.Export received
    Assert.Equal(Library.Exporting, retry.ExportState)
    Assert.Equal(Some Library.SendExport, effect)

[<Fact; Trait("Task", "T008")>]
let ``failures clear progress without claiming success and stray callbacks are ignored`` () =
    let started, _ = Library.update Library.Export Library.initial
    for failure in [Library.EmptyExport; Library.ExportDocumentUnavailable "source.pdf"; Library.ExportRequestFailed] do
        let failed, effect = Library.update (Library.Exported (Error failure)) started
        Assert.Equal(Library.ExportError failure, failed.ExportState)
        Assert.True(effect.IsNone)
        let stale, _ = Library.update (Library.Exported (Ok ())) failed
        Assert.Equal(failed, stale)

[<Theory; InlineData(409, """{"code":"empty_library"}"""); InlineData(409, """{"code":"document_unavailable","name":"source.pdf"}"""); InlineData(500, """{"code":"export_failed","detail":"private-path"}"""); Trait("Task", "T008")>]
let ``export problem parsing retains only approved error information`` status body =
    let failure = Library.parseExportFailure status body
    let expected =
        if body.Contains("empty_library") then Library.EmptyExport
        elif body.Contains("document_unavailable") then Library.ExportDocumentUnavailable "source.pdf"
        else Library.ExportRequestFailed
    Assert.Equal(expected, failure)
    Assert.DoesNotContain("private-path", FailureTexts.export failure)

[<Fact; Trait("Task", "T008")>]
let ``malformed and untrusted problem bodies are generic`` () =
    for status, body in [409, "{}"; 409, """{"code":"document_unavailable","name":7}"""; 500, """{"code":"empty_library"}"""; 200, "broken"] do
        Assert.Equal(Library.ExportRequestFailed, Library.parseExportFailure status body)

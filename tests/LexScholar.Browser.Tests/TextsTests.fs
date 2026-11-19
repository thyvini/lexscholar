module LexScholar.Browser.Tests.TextsTests

open System
open Xunit
open LexScholar.Client

[<Fact; Trait("Task", "T001")>]
let ``page citations and counts use Portuguese formats`` () =
    Assert.Equal("p. 3", Texts.pages [3])
    Assert.Equal("pp. 4–6", Texts.pages [4; 5; 6])
    Assert.Equal("p. 3, p. 5", Texts.pages [3; 5])
    Assert.Equal("pp. 1–2, p. 4", Texts.pages [1; 2; 4])
    Assert.Equal("1 página", Texts.pageCount 1)
    Assert.Equal("8 páginas", Texts.pageCount 8)
    Assert.Equal("1 documento", Texts.documentCount 1)
    Assert.Equal("4 documentos", Texts.documentCount 4)

[<Fact; Trait("Task", "T001")>]
let ``date uses local time and fixed Brazilian format`` () =
    let value = DateTimeOffset(DateTime(2026, 11, 14, 18, 54, 0, DateTimeKind.Local))
    Assert.Equal("14/11/2026 18:54", Texts.dateTime value)

[<Fact; Trait("Task", "T001")>]
let ``glossary has the exact prescribed wording`` () =
    Assert.Equal("Gerado para sua revisão. Não verificado.", Texts.generated)
    Assert.Equal("Continuar com o ChatGPT", Texts.signIn)
    Assert.Equal("Usando seu plano ChatGPT", Texts.usingPlan)

[<Fact; Trait("Task", "T009")>]
let ``project and library failures have Portuguese messages and unknown codes are generic`` () =
    let libraryFailures = [ Library.NotAPdf; Library.CorruptPdf; Library.EncryptedPdf; Library.TooLarge; Library.TooManyPages; Library.DuplicateAssociation; Library.InvalidRole; Library.NotFound; Library.DocumentNotFound; Library.RequestFailed ]
    for failure in libraryFailures do Assert.False(System.String.IsNullOrWhiteSpace(FailureTexts.library failure))
    let projectFailures = [ Projects.InvalidName; Projects.DuplicateName; Projects.NotFound; Projects.RequestFailed ]
    for failure in projectFailures do Assert.False(System.String.IsNullOrWhiteSpace(FailureTexts.project failure))
    Assert.Equal("Esse arquivo não é um PDF. Escolha um arquivo PDF.", FailureTexts.library Library.NotAPdf)
    Assert.Equal(Texts.genericFailure, Library.parseFailure 400 "{\"code\":\"unknown\"}" |> FailureTexts.library)
    Assert.Equal(Texts.genericFailure, FailureTexts.project Projects.RequestFailed)

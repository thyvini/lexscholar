module LexScholar.Domain.Tests.DocumentTests

open System
open LexScholar.Domain
open Xunit

let private id = Guid.Parse("0f5b2c1e-8d4a-4f3b-9c2d-1a2b3c4d5e6f")
let private sha = "0fcd00dc1ad92711575dfde326cda6c56b8fd83b0a01462cab6bfd012dcbe93b"
let private importedUtc = DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero)

let private create sha256 storageKey byteLength pageCount =
    LibraryDocument.create id sha256 storageKey "valid.pdf" byteLength pageCount importedUtc

let private assertResult<'a when 'a: equality> (expected: 'a) (actual: 'a) =
    Assert.True((expected = actual), sprintf "Expected: %A\nActual: %A" expected actual)

let private assertError expected actual =
    assertResult (Error expected) (actual |> Result.map ignore)

[<Fact; Trait("Task", "T024")>]
let ``a document with valid fields is created unchanged`` () =
    let expected =
        { Id = id
          Sha256 = sha
          StorageKey = "k-0001"
          OriginalFileName = "valid.pdf"
          ByteLength = 753L
          PageCount = 2
          ImportedUtc = importedUtc }
    assertResult (Ok expected) (create sha "k-0001" 753L 2)

[<Theory; Trait("Task", "T024")>]
[<InlineData("")>]
[<InlineData("0fcd00dc")>]
[<InlineData("0FCD00DC1AD92711575DFDE326CDA6C56B8FD83B0A01462CAB6BFD012DCBE93B")>]
[<InlineData("0fcd00dc1ad92711575dfde326cda6c56b8fd83b0a01462cab6bfd012dcbe93g")>]
[<InlineData("0fcd00dc1ad92711575dfde326cda6c56b8fd83b0a01462cab6bfd012dcbe93b0")>]
[<InlineData(null)>]
let ``Sha256 must be exactly 64 lowercase hex characters`` (value: string) =
    assertError InvalidSha256 (create value "k-0001" 753L 2)

[<Theory; Trait("Task", "T024")>]
[<InlineData("")>]
[<InlineData("  ")>]
[<InlineData(null)>]
let ``storage key is required`` (value: string) =
    assertError InvalidStorageKey (create sha value 753L 2)

[<Theory; Trait("Task", "T024")>]
[<InlineData(0L)>]
[<InlineData(-1L)>]
let ``byte length must be positive`` (value: int64) =
    assertError NonPositiveByteLength (create sha "k-0001" value 2)

[<Theory; Trait("Task", "T024")>]
[<InlineData(0)>]
[<InlineData(-1)>]
let ``page count must be positive`` (value: int) =
    assertError NonPositivePageCount (create sha "k-0001" 753L value)

[<Fact; Trait("Task", "T024")>]
let ``documents above the default import limits remain valid`` () =
    // Limits are checked only when importing; lowering them later must not
    // invalidate documents accepted earlier.
    let actual = create sha "k-0001" 100_000_001L 2_001
    assertResult (Ok (100_000_001L, 2_001)) (actual |> Result.map (fun document -> document.ByteLength, document.PageCount))

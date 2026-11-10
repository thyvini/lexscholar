module LexScholar.Infrastructure.Tests.PdfValidationTests

open System
open System.IO
open System.Threading
open LexScholar.Application
open LexScholar.Infrastructure
open Xunit

let private defaults = { MaxBytes = 100_000_000L; MaxPages = 2_000 }
let private validSha = "0fcd00dc1ad92711575dfde326cda6c56b8fd83b0a01462cab6bfd012dcbe93b"
let private distinctSha = "a0ba5d689dd633805ff9d5e6a77712204ca9b6d7b0e6b728ea58d8b66ce96694"
let private validPdf = Ok { Sha256 = validSha; ByteLength = 753L; PageCount = 2 }

let private fixture name = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))

/// A seekable, read-only input that records disposal and how many bytes were read.
/// In a MemoryStream subclass, span and async reads reach Read(byte[], int, int).
type private TrackingInput(bytes: byte array) =
    inherit MemoryStream(bytes, false)
    let mutable disposed = false
    let mutable bytesRead = 0L
    member _.Disposed = disposed
    member _.BytesRead = bytesRead
    override this.Read(buffer: byte array, offset, count) =
        let read = base.Read(buffer, offset, count)
        bytesRead <- bytesRead + int64 read
        read
    override this.Dispose(disposing) =
        if disposing then disposed <- true
        base.Dispose(disposing)

type private NonSeekableInput(bytes: byte array) =
    inherit MemoryStream(bytes, false)
    override _.CanSeek = false

let private run limits (input: Stream) =
    (PdfValidator.validate input limits CancellationToken.None).GetAwaiter().GetResult()

let private validate limits name =
    use input = new TrackingInput(fixture name)
    run limits input

let private assertResult<'a when 'a: equality> (expected: 'a) (actual: 'a) =
    Assert.True((expected = actual), sprintf "Expected: %A\nActual: %A" expected actual)

[<Fact; Trait("Task", "T020")>]
let ``a valid PDF is accepted with its SHA-256, byte length and page count`` () =
    assertResult validPdf (validate defaults "valid.pdf")

[<Fact; Trait("Task", "T020")>]
let ``a different valid PDF is accepted with a different SHA-256`` () =
    assertResult (Ok { Sha256 = distinctSha; ByteLength = int64 (fixture "distinct.pdf").Length; PageCount = 2 }) (validate defaults "distinct.pdf")

[<Fact; Trait("Task", "T020")>]
let ``a file without the PDF signature is rejected as not a PDF`` () =
    assertResult (Error NotPdf) (validate defaults "not-a-pdf.txt")

[<Fact; Trait("Task", "T020")>]
let ``an empty input is rejected as not a PDF`` () =
    use input = new TrackingInput([||])
    assertResult (Error NotPdf) (run defaults input)

[<Fact; Trait("Task", "T020")>]
let ``a truncated PDF is rejected as corrupt`` () =
    assertResult (Error Corrupt) (validate defaults "corrupt.pdf")

[<Fact; Trait("Task", "T020")>]
let ``an encrypted PDF is rejected as encrypted`` () =
    assertResult (Error Encrypted) (validate defaults "encrypted.pdf")

[<Fact; Trait("Task", "T020")>]
let ``a PDF of exactly the configured byte limit is accepted`` () =
    assertResult validPdf (validate { defaults with MaxBytes = 753L } "valid.pdf")

[<Fact; Trait("Task", "T020")>]
let ``a PDF one byte over the configured byte limit is rejected as too large`` () =
    assertResult (Error (TooLarge 752L)) (validate { defaults with MaxBytes = 752L } "valid.pdf")

[<Fact; Trait("Task", "T020")>]
let ``an over-limit input is rejected after reading exactly MaxBytes plus one bytes`` () =
    let bytes = Array.append (fixture "valid.pdf") (Array.zeroCreate 1_000_000)
    use input = new TrackingInput(bytes)
    assertResult (Error (TooLarge 1_000L)) (run { defaults with MaxBytes = 1_000L } input)
    // Exactly one byte past the limit proves the excess without reading the rest.
    Assert.Equal(1_001L, input.BytesRead)

[<Fact; Trait("Task", "T020")>]
let ``a PDF with exactly the configured page limit is accepted`` () =
    assertResult validPdf (validate { defaults with MaxPages = 2 } "valid.pdf")

[<Fact; Trait("Task", "T020")>]
let ``a PDF over the configured page limit is rejected with too many pages`` () =
    assertResult (Error (TooManyPages 1)) (validate { defaults with MaxPages = 1 } "valid.pdf")

[<Fact; Trait("Task", "T020")>]
let ``validation reads the input from the start whatever its position`` () =
    use input = new TrackingInput(fixture "valid.pdf")
    input.Position <- 100L
    assertResult validPdf (run defaults input)

[<Fact; Trait("Task", "T020")>]
let ``validation leaves the caller-owned input open after acceptance and rejection`` () =
    for name in [ "valid.pdf"; "not-a-pdf.txt"; "corrupt.pdf"; "encrypted.pdf" ] do
        use input = new TrackingInput(fixture name)
        run defaults input |> ignore
        Assert.False(input.Disposed, $"{name}: input was disposed.")
        Assert.True(input.CanRead, $"{name}: input is no longer readable.")

[<Fact; Trait("Task", "T020")>]
let ``a non-seekable input is refused`` () =
    use input = new NonSeekableInput(fixture "valid.pdf")
    Assert.Throws<ArgumentException>(fun () -> run defaults input |> ignore) |> ignore

[<Fact; Trait("Task", "T020")>]
let ``a cancelled validation stops with OperationCanceledException`` () =
    use input = new TrackingInput(fixture "valid.pdf")
    use cancellation = new CancellationTokenSource()
    cancellation.Cancel()
    Assert.ThrowsAny<OperationCanceledException>(fun () ->
        (PdfValidator.validate input defaults cancellation.Token).GetAwaiter().GetResult() |> ignore) |> ignore

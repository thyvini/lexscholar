// Regenerates tests/Fixtures/summary/*.pdf (F003 reference set, research §7).
//   dotnet fsi scripts/make-summary-fixtures.fsx
// Text is short public-domain passages embedded below (no download), repeated with
// a section number so every page differs. Sources are named in tests/Fixtures/README.md.
#r "nuget: PdfPig, 0.1.16"

open System
open System.IO
open System.Text
open UglyToad.PdfPig
open UglyToad.PdfPig.Content
open UglyToad.PdfPig.Core
open UglyToad.PdfPig.Writer
open UglyToad.PdfPig.Fonts.Standard14Fonts

let english =
    [ "It is a truth universally acknowledged, that a single man in possession of a good fortune, must be in want of a wife."
      "However little known the feelings or views of such a man may be on his first entering a neighbourhood, this truth is so well fixed in the minds of the surrounding families, that he is considered the rightful property of some one or other of their daughters."
      "Call me Ishmael. Some years ago, never mind how long precisely, having little or no money in my purse, and nothing particular to interest me on shore, I thought I would sail about a little and see the watery part of the world."
      "We hold these truths to be self-evident, that all men are created equal, that they are endowed by their Creator with certain unalienable Rights, that among these are Life, Liberty and the pursuit of Happiness." ]

// Plain ASCII on purpose: the standard Helvetica font of the builder has no accents.
let portuguese =
    [ "Uma noite destas, vindo da cidade para o Engenho Novo, encontrei no trem da Central um rapaz aqui do bairro, que eu conheco de vista e de chapeu."
      "Cumprimentou-me, sentou-se ao pe de mim, falou da lua e dos ministros, e acabou recitando-me versos."
      "A viagem era curta, e os versos pareciam longos, tanto mais longos quanto que eram de um poeta que eu nao conhecia." ]

let outDir = Path.Combine(__SOURCE_DIRECTORY__, "..", "tests", "Fixtures", "summary") |> Path.GetFullPath
Directory.CreateDirectory outDir |> ignore

let wrap (width: int) (text: string) =
    text.Split(' ')
    |> Array.fold (fun (lines: string list, current: string) word ->
        if current.Length + word.Length + 1 > width then (current :: lines, word)
        else (lines, if current = "" then word else current + " " + word)) ([], "")
    |> fun (lines, last) -> List.rev (last :: lines)

let font (builder: PdfDocumentBuilder) = builder.AddStandard14Font(Standard14Font.Helvetica)

let textPage (builder: PdfDocumentBuilder) f (passages: string list) (number: int) (paragraphs: int) =
    let page = builder.AddPage(595.0, 842.0)
    let body =
        [ for i in 0 .. paragraphs - 1 -> passages.[(number + i) % passages.Length] ]
    let lines = [ yield $"Section {number}"; yield ""; for p in body do yield! wrap 80 p; yield "" ]
    lines |> List.iteri (fun i line ->
        if line <> "" then page.AddText(line, 11.0, PdfPoint(50.0, 790.0 - 16.0 * float i), f) |> ignore)

let emptyPage (builder: PdfDocumentBuilder) =
    // A page with a drawn box and no text layer, like a scan.
    let page = builder.AddPage(595.0, 842.0)
    page.DrawRectangle(PdfPoint(50.0, 50.0), 495.0, 742.0, 1.0) |> ignore

let write name (build: PdfDocumentBuilder -> PdfDocumentBuilder.AddedFont -> unit) =
    let builder = new PdfDocumentBuilder()
    build builder (font builder)
    File.WriteAllBytes(Path.Combine(outDir, name), builder.Build())

write "article-en.pdf" (fun b f -> for n in 1 .. 12 do textPage b f english n 3)
write "article-pt.pdf" (fun b f -> for n in 1 .. 8 do textPage b f portuguese n 3)
write "long-40.pdf" (fun b f -> for n in 1 .. 40 do textPage b f english n 4)
write "scanned.pdf" (fun b _ -> for _ in 1 .. 3 do emptyPage b)
write "partial.pdf" (fun b f -> for n in 1 .. 6 do if n = 2 || n = 5 then emptyPage b else textPage b f english n 3)
write "little-text.pdf" (fun b f ->
    let page = b.AddPage(595.0, 842.0)
    page.AddText("Short note.", 11.0, PdfPoint(50.0, 790.0), f) |> ignore)
write "too-long.pdf" (fun b f -> for n in 1 .. 101 do textPage b f english n 1)

// Check what the tests rely on: page counts and which pages carry text.
for file in Directory.GetFiles(outDir, "*.pdf") |> Array.sort do
    use pdf = PdfDocument.Open(file)
    let withText = pdf.GetPages() |> Seq.filter (fun p -> p.Text.Trim().Length > 0) |> Seq.length
    printfn "%s: %d pages, %d with text, %d bytes" (Path.GetFileName file) pdf.NumberOfPages withText (FileInfo(file).Length)

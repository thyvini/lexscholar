#r "nuget: PdfPig, 0.1.16"
open System
open System.IO
open System.Diagnostics
open System.Text.Json
open UglyToad.PdfPig
open UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor
let sw=Stopwatch.StartNew()
use pdf=PdfDocument.Open("synthetic-2000.pdf")
use output=new StreamWriter("pages.jsonl")
let mutable chars=0
for n in 1..pdf.NumberOfPages do
    let text=ContentOrderTextExtractor.GetText(pdf.GetPage(n))
    chars<-chars+text.Length
    output.WriteLine(JsonSerializer.Serialize((n,text)))
output.Flush()
printfn "pages=%d chars=%d extraction_ms=%d" pdf.NumberOfPages chars sw.ElapsedMilliseconds
printfn "%s" (File.ReadLines("/proc/self/status") |> Seq.find (fun s -> s.StartsWith("VmHWM:")))

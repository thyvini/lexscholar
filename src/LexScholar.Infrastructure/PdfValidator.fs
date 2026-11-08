module LexScholar.Infrastructure.PdfValidator

open System
open LexScholar.Application

let validate : ValidatePdf =
    fun _ _ _ -> task { return raise (NotImplementedException("PDF validation is not implemented.")) }

module LexScholar.Infrastructure.PdfValidator

open LexScholar.Application

// RED stub: accepts everything without reading the input (T027).
let validate : ValidatePdf =
    fun _ _ _ -> task { return Ok { Sha256 = ""; ByteLength = 0L; PageCount = 0 } }

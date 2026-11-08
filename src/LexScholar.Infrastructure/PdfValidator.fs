namespace LexScholar.Infrastructure

open System
open LexScholar.Application

type PdfValidator() =
    interface IPdfValidator with
        member _.Validate(_, _, _) = raise (NotImplementedException())

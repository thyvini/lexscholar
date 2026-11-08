module LexScholar.Infrastructure.VerifiedDocumentContent

open System
open LexScholar.Application

let openRead : OpenVerifiedContent =
    fun _ _ -> task { return raise (NotImplementedException("Verified document opening is not implemented.")) }

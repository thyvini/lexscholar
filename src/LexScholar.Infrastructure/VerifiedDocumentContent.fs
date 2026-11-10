module LexScholar.Infrastructure.VerifiedDocumentContent

open LexScholar.Application

let openRead (dataDirectory: string) : OpenVerifiedContent =
    ignore dataDirectory
    fun _ _ -> task { return Error DocumentNotFound }

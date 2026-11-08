namespace LexScholar.Infrastructure

open System
open LexScholar.Application

type VerifiedDocumentContent() =
    interface IVerifiedDocumentContent with
        member _.Open(_, _) = raise (NotImplementedException())

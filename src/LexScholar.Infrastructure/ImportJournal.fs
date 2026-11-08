namespace LexScholar.Infrastructure

open System
open LexScholar.Application

type ImportJournal() =
    interface IImportJournal with
        member _.Create(_, _) = raise (NotImplementedException())
        member _.Update(_, _) = raise (NotImplementedException())
        member _.Remove(_, _) = raise (NotImplementedException())
        member _.Recover _ = raise (NotImplementedException())

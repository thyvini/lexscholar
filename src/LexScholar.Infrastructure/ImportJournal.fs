module LexScholar.Infrastructure.ImportJournal

open System
open LexScholar.Application

let unimplemented : ImportJournalOperations =
    { Create = fun _ _ -> task { return raise (NotImplementedException("Import journal creation is not implemented.")) }
      Update = fun _ _ -> task { return raise (NotImplementedException("Import journal updates are not implemented.")) }
      Remove = fun _ _ -> task { return raise (NotImplementedException("Import journal removal is not implemented.")) }
      Recover = fun _ -> task { return raise (NotImplementedException("Import recovery is not implemented.")) } }

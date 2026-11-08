module LexScholar.Application.Projects

open System
open LexScholar.Application

// Each invocation faults explicitly; constructing this record performs no I/O.
let unimplemented : ProjectOperations =
    { List = fun _ -> task { return raise (NotImplementedException("Project listing is not implemented.")) }
      Create = fun _ _ -> task { return raise (NotImplementedException("Project creation is not implemented.")) }
      Select = fun _ _ -> task { return raise (NotImplementedException("Project selection is not implemented.")) } }

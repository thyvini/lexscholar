module LexScholar.Application.Library

open System
open LexScholar.Application

let unimplemented : LibraryOperations =
    { List = fun _ -> task { return raise (NotImplementedException("Library listing is not implemented.")) }
      Import = fun _ _ _ -> task { return raise (NotImplementedException("PDF import is not implemented.")) }
      ListForProject = fun _ _ -> task { return raise (NotImplementedException("Project document listing is not implemented.")) }
      Associate = fun _ _ _ _ -> task { return raise (NotImplementedException("Document association is not implemented.")) }
      ChangeRole = fun _ _ _ _ -> task { return raise (NotImplementedException("Role changes are not implemented.")) }
      RemoveAssociation = fun _ _ _ -> task { return raise (NotImplementedException("Association removal is not implemented.")) } }

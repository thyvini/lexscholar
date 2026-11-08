namespace LexScholar.Application

open System

// Fail explicitly rather than returning empty collections or fabricated data.
type ProjectService() =
    interface IProjectService with
        member _.List _ = raise (NotImplementedException())
        member _.Create(_, _) = raise (NotImplementedException())
        member _.Select(_, _) = raise (NotImplementedException())

type LibraryService() =
    interface ILibraryService with
        member _.List _ = raise (NotImplementedException())
        member _.Import(_, _, _) = raise (NotImplementedException())
        member _.ListForProject(_, _) = raise (NotImplementedException())
        member _.Associate(_, _, _, _) = raise (NotImplementedException())
        member _.ChangeRole(_, _, _, _) = raise (NotImplementedException())
        member _.RemoveAssociation(_, _, _) = raise (NotImplementedException())

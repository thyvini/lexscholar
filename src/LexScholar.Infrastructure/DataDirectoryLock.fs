namespace LexScholar.Infrastructure

open System

module DataDirectoryLock =
    // T008 will implement OS-held exclusivity and ownership.
    let acquire (_dataDirectory: string) : IDisposable =
        raise (NotImplementedException())

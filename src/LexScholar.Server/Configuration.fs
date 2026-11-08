namespace LexScholar.Server

open System
open Microsoft.Extensions.Configuration
open LexScholar.Application

type WorkspaceConfiguration =
    { DataDirectory: string
      ImportLimits: ImportLimits }

module Configuration =
    // Validation and defaults are deliberately deferred to T007.
    let load (_configuration: IConfiguration) : WorkspaceConfiguration =
        raise (NotImplementedException())

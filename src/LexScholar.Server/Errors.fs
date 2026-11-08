namespace LexScholar.Server

open System
open System.Collections.Generic
open Microsoft.AspNetCore.Http

module Errors =
    // Never include exception details, request paths, or filesystem paths.
    let problem (context: HttpContext) (status: int) title code =
        let extensions = Dictionary<string, obj>()
        extensions.Add("code", code)
        Results.Problem(statusCode = Nullable status, title = title, extensions = extensions).ExecuteAsync(context)

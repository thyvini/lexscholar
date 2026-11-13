/// The sign-in record and the welcome marker on disk (F005, data-model.md, research.md section 6).
module LexScholar.Infrastructure.ChatGptCredentials

open LexScholar.Domain

let read (dataDirectory: string) : Credentials option = None
let save (dataDirectory: string) (credentials: Credentials) : unit = ()
let delete (dataDirectory: string) : unit = ()
let isShown (dataDirectory: string) : bool = false
let markShown (dataDirectory: string) : unit = ()

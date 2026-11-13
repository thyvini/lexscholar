/// The sign-in record and the welcome marker on disk (F005, data-model.md, research.md section 6).
/// The record holds secrets: it is created owner-only, replaced by rename, and never logged.
module LexScholar.Infrastructure.ChatGptCredentials

open System
open System.IO
open System.Text.Json
open LexScholar.Domain

let private recordPath (dataDirectory: string) = Path.Combine(dataDirectory, "chatgpt-signin.json")
let private markerPath (dataDirectory: string) = Path.Combine(dataDirectory, "chatgpt-welcome-shown")
let private ownerOnly = UnixFileMode.UserRead ||| UnixFileMode.UserWrite

let private serialize (credentials: Credentials) : byte array =
    use stream = new MemoryStream()
    do
        use writer = new Utf8JsonWriter(stream)
        writer.WriteStartObject()
        writer.WriteString("clientId", credentials.ClientId)
        writer.WriteString("hostId", credentials.HostId)
        writer.WriteString("subject", credentials.Subject)
        writer.WriteString("account", credentials.Account)
        writer.WriteString("accessToken", credentials.AccessToken)
        writer.WriteString("refreshToken", credentials.RefreshToken)
        writer.WriteString("idToken", credentials.IdToken)
        writer.WriteString("expiresAt", credentials.ExpiresAt)
        writer.WriteStartArray "scopes"
        for scope in credentials.Scopes do writer.WriteStringValue scope
        writer.WriteEndArray()
        writer.WriteString("signedInAt", credentials.SignedInAt)
        credentials.ConfirmedAt |> Option.iter (fun at -> writer.WriteString("confirmedAt", at))
        writer.WriteEndObject()
    stream.ToArray()

let private parse (bytes: byte array) : Credentials =
    use document = JsonDocument.Parse bytes
    let root = document.RootElement
    let text (name: string) = root.GetProperty(name).GetString() |> Option.ofObj |> Option.get
    let time (name: string) = root.GetProperty(name).GetDateTimeOffset()
    { ClientId = text "clientId"
      HostId = text "hostId"
      Subject = text "subject"
      Account = text "account"
      AccessToken = text "accessToken"
      RefreshToken = text "refreshToken"
      IdToken = text "idToken"
      ExpiresAt = time "expiresAt"
      Scopes = [ for scope in root.GetProperty("scopes").EnumerateArray() -> scope.GetString() |> Option.ofObj |> Option.get ]
      SignedInAt = time "signedInAt"
      ConfirmedAt = (match root.TryGetProperty "confirmedAt" with | true, value -> Some(value.GetDateTimeOffset()) | _ -> None) }

let delete (dataDirectory: string) : unit = File.Delete(recordPath dataDirectory)

/// The stored sign-in, or `None` when absent. An unreadable or malformed file counts as
/// signed out and is deleted.
let read (dataDirectory: string) : Credentials option =
    let path = recordPath dataDirectory
    if not (File.Exists path) then None else
    try Some(parse (File.ReadAllBytes path))
    with _ ->
        try delete dataDirectory with _ -> ()
        None

/// Writes to a temporary file created with mode 0600 (never readable by others, not even
/// briefly), flushes it, then renames it over the record. A failure leaves the previous
/// record in place.
let save (dataDirectory: string) (credentials: Credentials) : unit =
    let bytes = serialize credentials
    let temporary = Path.Combine(dataDirectory, $"chatgpt-signin.json.{Guid.NewGuid():N}.tmp")
    try
        let options = FileStreamOptions(Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, UnixCreateMode = Nullable ownerOnly)
        do
            use stream = new FileStream(temporary, options)
            stream.Write(bytes, 0, bytes.Length)
            stream.Flush true
        File.Move(temporary, recordPath dataDirectory, true)
    with _ ->
        try File.Delete temporary with _ -> ()
        reraise ()

let isShown (dataDirectory: string) : bool = File.Exists(markerPath dataDirectory)

let markShown (dataDirectory: string) : unit =
    let options = FileStreamOptions(Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = Nullable ownerOnly)
    (new FileStream(markerPath dataDirectory, options)).Dispose()

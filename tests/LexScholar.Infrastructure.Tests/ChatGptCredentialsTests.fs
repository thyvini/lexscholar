/// The sign-in record and the welcome marker on disk (F005 T002, research.md section 6).
module LexScholar.Infrastructure.Tests.ChatGptCredentialsTests

open System
open System.IO
open LexScholar.Domain
open LexScholar.Infrastructure
open LexScholar.Infrastructure.Tests.SchemaTestSupport
open Xunit

let private signInPath (directory: string) = Path.Combine(directory, "chatgpt-signin.json")
let private markerPath (directory: string) = Path.Combine(directory, "chatgpt-welcome-shown")
let private ownerOnly = UnixFileMode.UserRead ||| UnixFileMode.UserWrite

let private credentials : Credentials =
    { ClientId = "client-1"; HostId = "urn:uuid:host"; Subject = "user-1"; Account = "a@b.test"
      AccessToken = "access-1"; RefreshToken = "refresh-1"; IdToken = "id-1"
      ExpiresAt = DateTimeOffset(2026, 11, 13, 13, 0, 0, TimeSpan.Zero)
      Scopes = [ "openid"; "chatgpt.tokens.use.direct" ]
      SignedInAt = DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero)
      ConfirmedAt = None }

[<Fact; Trait("Task", "T002")>]
let ``a saved record reads back unchanged, with and without confirmation`` () =
    use directory = new TemporaryDirectory()
    ChatGptCredentials.save directory.Path credentials
    Assert.Equal(Some credentials, ChatGptCredentials.read directory.Path)
    let confirmed = { credentials with ConfirmedAt = Some(DateTimeOffset(2026, 11, 13, 12, 5, 0, TimeSpan.Zero)) }
    ChatGptCredentials.save directory.Path confirmed
    Assert.Equal(Some confirmed, ChatGptCredentials.read directory.Path)

[<Fact; Trait("Task", "T002")>]
let ``the record uses the field names of the data model`` () =
    use directory = new TemporaryDirectory()
    ChatGptCredentials.save directory.Path credentials
    let json = File.ReadAllText(signInPath directory.Path)
    for name in [ "clientId"; "hostId"; "subject"; "account"; "accessToken"; "refreshToken"; "idToken"; "expiresAt"; "scopes"; "signedInAt" ] do
        Assert.Contains($"\"{name}\"", json)
    Assert.DoesNotContain("confirmedAt", json)

[<Fact; Trait("Task", "T002")>]
let ``the file is owner-only and no temporary file is left`` () =
    use directory = new TemporaryDirectory()
    ChatGptCredentials.save directory.Path credentials
    ChatGptCredentials.save directory.Path { credentials with RefreshToken = "refresh-2" }
    Assert.Equal(ownerOnly, File.GetUnixFileMode(signInPath directory.Path))
    Assert.Equal<string list>([ "chatgpt-signin.json" ], Directory.GetFileSystemEntries directory.Path |> Array.map Path.GetFileName |> List.ofArray)

[<Fact; Trait("Task", "T002")>]
let ``a failed write leaves the previous file intact`` () =
    use directory = new TemporaryDirectory()
    ChatGptCredentials.save directory.Path credentials
    // With the directory read-only the temporary file cannot be created.
    File.SetUnixFileMode(directory.Path, UnixFileMode.UserRead ||| UnixFileMode.UserExecute)
    try
        Assert.ThrowsAny<exn>(fun () -> ChatGptCredentials.save directory.Path { credentials with AccessToken = "new" }) |> ignore
    finally
        File.SetUnixFileMode(directory.Path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
    Assert.Equal(Some credentials, ChatGptCredentials.read directory.Path)

[<Fact; Trait("Task", "T002")>]
let ``a malformed file counts as signed out and is deleted`` () =
    use directory = new TemporaryDirectory()
    for content in [ "not json"; "{}"; "{\"clientId\":\"x\"}"; "" ] do
        File.WriteAllText(signInPath directory.Path, content)
        Assert.Equal(None, ChatGptCredentials.read directory.Path)
        Assert.False(File.Exists(signInPath directory.Path), $"not deleted: {content}")

[<Fact; Trait("Task", "T002")>]
let ``an absent file is signed out, not an error, and delete of nothing is fine`` () =
    use directory = new TemporaryDirectory()
    Assert.Equal(None, ChatGptCredentials.read directory.Path)
    ChatGptCredentials.delete directory.Path

[<Fact; Trait("Task", "T002")>]
let ``delete removes the record but keeps the welcome marker`` () =
    use directory = new TemporaryDirectory()
    ChatGptCredentials.save directory.Path credentials
    Assert.False(ChatGptCredentials.isShown directory.Path)
    ChatGptCredentials.markShown directory.Path
    ChatGptCredentials.markShown directory.Path
    Assert.True(ChatGptCredentials.isShown directory.Path)
    ChatGptCredentials.delete directory.Path
    Assert.False(File.Exists(signInPath directory.Path))
    Assert.Equal(None, ChatGptCredentials.read directory.Path)
    Assert.True(ChatGptCredentials.isShown directory.Path)
    Assert.Equal(0L, FileInfo(markerPath directory.Path).Length)

[<Fact; Trait("Task", "T002")>]
let ``delete also removes a temporary file left by an interrupted save`` () =
    use directory = new TemporaryDirectory()
    let leftover = Path.Combine(directory.Path, "chatgpt-signin.json.0123abcd.tmp")
    File.WriteAllText(leftover, "secret")
    ChatGptCredentials.save directory.Path credentials
    ChatGptCredentials.delete directory.Path
    Assert.Empty(Directory.GetFileSystemEntries directory.Path)

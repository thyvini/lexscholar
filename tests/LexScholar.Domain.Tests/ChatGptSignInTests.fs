/// Pure sign-in rules for ChatGPT (F005 T002, research.md sections 3-5, 7, 9).
module LexScholar.Domain.Tests.ChatGptSignInTests

open System
open System.Web
open LexScholar.Domain
open LexScholar.Domain.ChatGptSignIn
open Xunit

let private now = DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero)

[<Fact; Trait("Task", "T002")>]
let ``the challenge is the RFC 7636 example, base64url without padding`` () =
    Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", challengeOf "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")

let private query (url: string) =
    let uri = Uri url
    uri, HttpUtility.ParseQueryString uri.Query

[<Fact; Trait("Task", "T002")>]
let ``the first authorization address carries every parameter and registration hints`` () =
    let url = authorizationUrl "dynamic_agent_client" "http://127.0.0.1:5000/auth/callback" "st&te" "n once" "chal" (Some "urn:uuid:abc")
    let uri, q = query url
    Assert.Equal(authorizationEndpoint, uri.GetLeftPart UriPartial.Path)
    Assert.Equal("code", q["response_type"])
    Assert.Equal("dynamic_agent_client", q["client_id"])
    Assert.Equal("http://127.0.0.1:5000/auth/callback", q["redirect_uri"])
    Assert.Equal("openid profile email offline_access resource.invoke chatgpt.tokens.use.direct", q["scope"])
    Assert.Equal("https://api.openai.com/v1", q["resource"])
    Assert.Equal("st&te", q["state"])
    Assert.Equal("n once", q["nonce"])
    Assert.Equal("chal", q["code_challenge"])
    Assert.Equal("S256", q["code_challenge_method"])
    Assert.Equal("LexScholar", q["agent_name_hint"])
    Assert.Equal("urn:uuid:abc", q["ext_agent_host_id"])
    Assert.DoesNotContain("st&te", url)
    Assert.DoesNotContain(" ", url)

[<Fact; Trait("Task", "T002")>]
let ``a later authorization address has no registration hints`` () =
    let _, q = query (authorizationUrl "issued-id" "http://127.0.0.1:5000/auth/callback" "s" "n" "c" None)
    Assert.Equal("issued-id", q["client_id"])
    Assert.Null q["agent_name_hint"]
    Assert.Null q["ext_agent_host_id"]

let private claims (iss: string) (aud: string) (exp: DateTimeOffset) (nonce: string) (extra: string) =
    $"""{{"iss":"{iss}","aud":"{aud}","exp":{exp.ToUnixTimeSeconds()},"nonce":"{nonce}"{extra}}}"""

let private good extra = claims "https://auth.openai.com" "cid" (now.AddMinutes 5.) "nn" extra

[<Fact; Trait("Task", "T002")>]
let ``valid claims give the subject and the email as account`` () =
    let result = validateIdToken now "cid" "nn" (good ",\"sub\":\"user-1\",\"email\":\"a@b.test\",\"name\":\"A B\"")
    Assert.Equal(Ok { Subject = "user-1"; Account = "a@b.test" }, result)

[<Fact; Trait("Task", "T002")>]
let ``the account falls back to name and then to a generic label`` () =
    Assert.Equal(Ok { Subject = "u"; Account = "A B" }, validateIdToken now "cid" "nn" (good ",\"sub\":\"u\",\"name\":\"A B\""))
    Assert.Equal(Ok { Subject = "u"; Account = "your ChatGPT account" }, validateIdToken now "cid" "nn" (good ",\"sub\":\"u\""))

[<Fact; Trait("Task", "T002")>]
let ``each claim failure is refused`` () =
    let sub = ",\"sub\":\"u\""
    Assert.Equal(Error WrongIssuer, validateIdToken now "cid" "nn" (claims "https://evil.test" "cid" (now.AddMinutes 5.) "nn" sub))
    Assert.Equal(Error WrongAudience, validateIdToken now "cid" "nn" (claims "https://auth.openai.com" "other" (now.AddMinutes 5.) "nn" sub))
    Assert.Equal(Error Expired, validateIdToken now "cid" "nn" (claims "https://auth.openai.com" "cid" (now.AddSeconds -1.) "nn" sub))
    Assert.Equal(Error Expired, validateIdToken now "cid" "nn" (claims "https://auth.openai.com" "cid" now "nn" sub))
    Assert.Equal(Error NonceMismatch, validateIdToken now "cid" "nn" (claims "https://auth.openai.com" "cid" (now.AddMinutes 5.) "zz" sub))
    Assert.Equal(Error Malformed, validateIdToken now "cid" "nn" (good ""))
    Assert.Equal(Error Malformed, validateIdToken now "cid" "nn" "not json")
    Assert.Equal(Error Malformed, validateIdToken now "cid" "nn" "[]")

[<Fact; Trait("Task", "T002")>]
let ``an audience list containing the client id is accepted`` () =
    let json = $"""{{"iss":"https://auth.openai.com","aud":["x","cid"],"exp":{now.AddMinutes(5.).ToUnixTimeSeconds()},"nonce":"nn","sub":"u"}}"""
    Assert.True(Result.isOk (validateIdToken now "cid" "nn" json))

[<Fact; Trait("Task", "T002")>]
let ``plan usage needs the direct token scope`` () =
    Assert.True(grantsPlanUsage "openid email chatgpt.tokens.use.direct offline_access")
    Assert.False(grantsPlanUsage "openid email offline_access")
    Assert.False(grantsPlanUsage "chatgpt.tokens.use.directly")
    Assert.False(grantsPlanUsage "")

let private credentials expiresAt : Credentials =
    { ClientId = "c"; HostId = "h"; Subject = "s"; Account = "a"; AccessToken = "at"; RefreshToken = "rt"; IdToken = "it"
      ExpiresAt = expiresAt; Scopes = [ "x" ]; SignedInAt = now; ConfirmedAt = None }

[<Fact; Trait("Task", "T002")>]
let ``refresh is needed when the access token expires within ten minutes`` () =
    Assert.True(needsRefresh now (credentials (now.AddMinutes 10.)))
    Assert.True(needsRefresh now (credentials (now.AddMinutes -1.)))
    Assert.False(needsRefresh now (credentials (now.AddMinutes 10. |> fun t -> t.AddSeconds 1.)))

[<Fact; Trait("Task", "T002")>]
let ``the model is the first listed entry`` () =
    let models = [ { Slug = "hidden"; Visibility = "hide" }; { Slug = "first"; Visibility = "list" }; { Slug = "second"; Visibility = "list" } ]
    Assert.Equal(Some "first", pickModel models)
    Assert.Equal(None, pickModel [ { Slug = "hidden"; Visibility = "hide" } ])
    Assert.Equal(None, pickModel [])

[<Fact; Trait("Task", "T002")>]
let ``an expiry outside the date range is malformed, not an exception`` () =
    let json seconds = $"""{{"iss":"https://auth.openai.com","aud":"cid","exp":{seconds},"nonce":"nn","sub":"u"}}"""
    Assert.Equal(Error Malformed, validateIdToken now "cid" "nn" (json "253402300800"))
    Assert.Equal(Error Malformed, validateIdToken now "cid" "nn" (json "-62135596801"))

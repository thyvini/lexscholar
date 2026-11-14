/// Client for OpenAI's sign-in service (F005, research.md sections 5 and 7).
module LexScholar.Infrastructure.OpenAiAuth

open System
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open LexScholar.Application
open LexScholar.Application.ChatGptAccess
open LexScholar.Domain

/// Why an ID token was refused.
type IdTokenFailure =
    | BadSignature
    | KeyUnavailable
    | Claims of IdTokenProblem

let createHandler () : HttpMessageHandler = new SocketsHttpHandler(AllowAutoRedirect = false) :> HttpMessageHandler

let exchange (http: HttpClient) (clock: unit -> DateTimeOffset) (clientId: string) (code: string) (verifier: string) (redirectUri: string) (cancellation: CancellationToken) : Task<Result<TokenGrant, unit>> =
    Task.FromResult(Error())

let refresh (http: HttpClient) (clock: unit -> DateTimeOffset) (credentials: Credentials) (cancellation: CancellationToken) : Task<Result<Credentials, RefreshFailure>> =
    Task.FromResult(Error Unreachable)

let revoke (http: HttpClient) (credentials: Credentials) (cancellation: CancellationToken) : Task<bool> = Task.FromResult false

let verifyIdToken (http: HttpClient) (clock: unit -> DateTimeOffset) (clientId: string) (nonce: string) (idToken: string) (cancellation: CancellationToken) : Task<Result<Identity, IdTokenFailure>> =
    Task.FromResult(Error BadSignature)

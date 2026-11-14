/// Client for ChatGPT's Responses API (F005 stub; filled in at GREEN).
module LexScholar.Infrastructure.ResponsesApi

open System.Net.Http
open System.Threading
open System.Threading.Tasks
open LexScholar.Application
open LexScholar.Domain
open Microsoft.Extensions.Logging

type Settings = { Model: string; AccessToken: string }

let complete (_logger: ILogger) (_client: HttpClient) (_settings: Settings) (_messages: Summarize.ChatMessage list) (_cancellation: CancellationToken)
    : Task<Result<string, Summarize.ServiceError>> =
    Task.FromResult(Error Summarize.ServiceError.UnusableOutput)

let listModels (_client: HttpClient) (_accessToken: string) (_cancellation: CancellationToken)
    : Task<Result<ModelEntry list, Summarize.ServiceError>> =
    Task.FromResult(Error Summarize.ServiceError.UnusableOutput)

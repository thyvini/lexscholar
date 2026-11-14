# Implementation Plan: Sign in with ChatGPT

**Branch**: `docs/f005-chatgpt-sign-in` | **Date**: 2026-11-13 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/005-chatgpt-sign-in/spec.md`

## Summary

The researcher signs in with her ChatGPT account once: the host starts the sign-in, she completes it in her browser, and the browser returns to LexScholar's own loopback address. The host keeps the resulting credentials in one owner-only file in the data directory and refreshes them as needed. A summary can then go to her ChatGPT plan: a second implementation of F003's `Complete` capability sends the same prompts to OpenAI's Responses service with her plan's credentials, so chunking, citations, validation, progress and abandoning are unchanged. The server enforces the destination choice, a once-per-sign-in confirmation, and the failure states (usage limit, signed out, no plan access). The client shows the account, "Using ChatGPT plan", the one-time welcome and "Manage usage". Decisions: [research.md](research.md); stored data: [data-model.md](data-model.md); endpoints: [contracts/chatgpt-api.yaml](contracts/chatgpt-api.yaml); validation: [quickstart.md](quickstart.md).

## Technical Context

**Language/Version**: F# on .NET 10 (SDK pinned in `global.json`)

**Primary Dependencies**: existing only: ASP.NET Core, Bolero, `System.Net.Http`, `System.Text.Json`, `System.Security.Cryptography` (PKCE, ID-token signature). **No new packages.**

**Storage**: one JSON file `chatgpt-signin.json` in the data directory (owner-only, atomic write) plus one empty marker file for the one-time welcome. No database change, no migration.

**Testing**: xUnit v3 in the existing projects; a fake OpenAI handler (auth, JWKS, models, Responses) injected through the existing outgoing-handler seam; Playwright for one sign-in journey; a manual validation with a real ChatGPT Plus or Pro account (the spike's account).

**Target Platform**: Linux, Firefox and Chromium (as F001).

**Project Type**: local web application (ASP.NET Core host + Bolero WebAssembly client).

**Performance Goals**: sign-in completes in under 5 minutes of her time (SC-001, manual). Summary speed is F003's; the spike measured 3.7 s for a one-sentence summary.

**Constraints**: incoming host security unchanged (loopback, Host/Origin, antiforgery on unsafe methods); outgoing calls only to `auth.openai.com` and `api.openai.com` over HTTPS, no redirects; credentials never in responses, logs, error messages, browser storage or the repository; no ChatGPT conversation data is requested.

**Scale/Scope**: one researcher, one ChatGPT account at a time, one sign-in attempt at a time.

## Constitution Check

*Checked before research and again after design: passes, no exceptions.*

| Principle | How the design meets it |
|---|---|
| I. Integrity, verifiable evidence | Unchanged: the same validation drops points without valid citations. The summary's context names "ChatGPT plan" and the model used (FR-014). |
| II. Privacy and control | Document text leaves her machine only after her explicit confirmation naming her ChatGPT plan, enforced by the server once per sign-in (FR-010); the notice says where the text goes and defers to her ChatGPT data controls without claiming more. Credentials are stored locally with owner-only permissions; sign-out revokes them at OpenAI and deletes the file (FR-004, SC-002). Nothing else is stored; F002 tracking (`$12y`) is unchanged. |
| III. Human oversight | Unchanged label "generated, for review". |
| IV. Testability, reproducibility | Fake OpenAI for every state (signed out, no plan, usage limit, refresh, disconnect); a manual run on her real account is the release gate. The summary shows service "ChatGPT plan" and model. |
| V. Simplicity | No new packages, no database, one new file for credentials, one new client for the Responses service behind the existing `Complete` seam; no settings screen, no model picker. The callback uses the host's own listener instead of a second one. |
| VI. Decisions recorded | research.md. |

## Design

**Domain** (`src/LexScholar.Domain/ChatGptSignIn.fs`, pure): `Credentials` record (see data-model.md); PKCE (`challengeOf verifier`), `authorizationUrl`, `validateIdToken` (issuer, audience, expiry, nonce → account), `grantsPlanUsage : scope -> bool`, `needsRefresh : now -> Credentials -> bool` (expires within 10 minutes), `pickModel : ModelEntry list -> string option` (first `visibility = list`), and `SignInOutcome = SignedIn | Cancelled | NoPlanAccess | Failed`.

**Application** (`src/LexScholar.Application/ChatGptAccess.fs`): workflows over capability functions: `startSignIn`, `completeSignIn` (callback: state check, exchange, validate, require plan scope, save), `current` (status), `accessToken` (before a summary starts, refresh when the access token expires within 10 minutes; terminal refresh errors clear the credentials and report `SignedOut`; a network failure keeps them), `signOut` (revoke best effort, always delete). One lock surrounds the stored credentials: saving a new sign-in, sign-out and refresh all take it, and a refresh saves only if the record still exists with the same refresh token, so a sign-out can never be undone by a refresh in flight. `Summarize.ServiceError` gains `UsageLimitReached`, `SignedOut`, `NoPlanAccess`; `failureDetail` explains each in LexScholar's words. `Summarize.summarize` is unchanged.

**Infrastructure**: `ChatGptCredentials.fs` (read, atomic write with mode 0600, delete; welcome marker); `OpenAiAuth.fs` (token exchange, refresh, revocation, JWKS, ID-token signature check; fixed hosts, no redirects, error text never echoed); `ResponsesApi.fs` (`complete` with the same shape as `ChatCompletions.complete`: system messages become `instructions`, `store: false`, `stream: true`, text deltas collected, success only on `response.completed`, error codes mapped; `listModels`).

**Server**: `ChatGptEndpoints.fs` (destinations, sign-in start, callback, sign-out, welcome); `SummaryEndpoints.fs` changed: `destination` in the request, the ChatGPT confirmation, access-token acquisition, a sign-out epoch token linked to the request so sign-out abandons a running summary, new failure codes. Registered in `Endpoints.fs`.

**Client**: `ChatGpt.fs` (state: `Unknown | SignedOut | SignedIn of account | Starting`, welcome pending, one-time return message; effects load, start sign-in, sign out, dismiss welcome); `Summary.fs` gains destination choice, the ChatGPT confirmation and the new failures; `App.fs` renders account line, "Continue with ChatGPT", sign-out, "Using ChatGPT plan", welcome, "Usage limit reached" with "Manage usage", and "signed out" with sign-in.

## Project Structure

```text
specs/005-chatgpt-sign-in/
├── plan.md  research.md  data-model.md  quickstart.md
├── contracts/chatgpt-api.yaml
└── tasks.md                     # next: /speckit-tasks

src/LexScholar.Domain/ChatGptSignIn.fs                     # new
src/LexScholar.Application/{ChatGptAccess.fs,Summarize.fs} # new, changed
src/LexScholar.Infrastructure/{ChatGptCredentials,OpenAiAuth,ResponsesApi}.fs  # new
src/LexScholar.Server/ChatGptEndpoints.fs                  # new
src/LexScholar.Server/{Endpoints,SummaryEndpoints}.fs       # changed
src/LexScholar.Client/ChatGpt.fs                           # new
src/LexScholar.Client/{Summary,App}.fs, wwwroot assets     # changed (approved logo)
tests/LexScholar.Server.Tests/FakeOpenAi.fs, TestHarness.fs  # fake service, harness option
tests/LexScholar.*.Tests/…ChatGpt…Tests.fs                 # per layer
docs/agents/codebase.md                                    # new modules and the fake service
```

## Size check

The design fits the budget as 12 vertical slices; `/speckit-tasks` produces the real list:

1. Setup: types, route and registration, fake OpenAI and harness option, `codebase.md`.
2. Foundations: pure sign-in rules and the credential file.
3. OpenAI sign-in client (exchange, refresh, revocation, ID-token check).
4. Researcher can sign in, with the lock around stored credentials; cancelled, failed and no-plan sign-ins store nothing.
5. Researcher can sign out; the one-time welcome.
6. Responses client and model listing.
7. Summary through her plan: destination choice, once-per-sign-in confirmation, refresh before a summary, the not-configured check.
8. Failure states and credential hygiene: usage limit, disconnected or expired sign-in, no plan access, nothing secret in logs or responses.
9. Client: sign-in, account, sign-out, welcome, return messages.
10. Client: destination choice, "Using ChatGPT plan", confirmation, "Manage usage", signed-out recovery, sign-in offered first when nothing is configured.
11. Browser journey with the fake service.
12. Manual validation on her real account.

## Complexity Tracking

None.

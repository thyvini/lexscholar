# Research: Sign in with ChatGPT (F005)

Technology decisions for [plan.md](plan.md). Sources: `docs/notes/summary-service-access.md` (research, spike on 2026-11-13) and OpenAI's Sign in with ChatGPT documentation for open-source and locally hosted apps (developers.openai.com/siwc, read 2026-11-13: sign-in, profiles and sessions, errors and recovery, models and inference, token reference, UI/UX guidelines). Points that the docs do not state are listed in section 12 and are confirmed in the first slices.

## 1. Where the browser returns: the host's own address

**Decision**: the authorization request's redirect address is `http://127.0.0.1:<port>/auth/callback`, a route of LexScholar's own host, with the port taken from the request that started the sign-in (the host is loopback-only, so this is always a loopback port). One attempt is pending at a time, held in memory (state, nonce, code verifier, redirect address, expiry 10 minutes). The callback checks the state, finishes the sign-in and redirects the browser to `/index.html?chatgpt=<outcome>` (not `/`: the host's root route redirects to `/index.html` and would drop the query). Nothing is saved until the token exchange and the ID-token checks succeed, so a host stopped in the middle of a sign-in leaves her signed out.

**Rationale**: the docs require an HTTP loopback callback on `127.0.0.1`, exact scheme, host and path, with only the port allowed to vary. A route on the existing host needs no second listener, no port search and no extra lifecycle. A GET navigation from the browser carries no `Origin` and is a safe method, so the host security rules apply unchanged. Port 1455 (the spike's) is also what the Codex tool uses, so it can be taken.

**Alternatives**: a temporary listener on 127.0.0.1:1455 as the spike did (proven, but a second listener outside the host security middleware, and a collision with other tools); an ephemeral-port listener (same cost).

**Risk and fallback**: the docs say only the port varies on later sign-ins; they do not say a first registration accepts an arbitrary port. If real registration refuses port 5000, fall back to the temporary listener on 127.0.0.1:1455. The first manual check (quickstart, step 1) settles it before slice 4 is built on it.

## 2. One sign-in, same tab

**Decision**: the client asks `POST /api/chatgpt/sign-in` for the authorization address and navigates the current tab to it. The callback redirects back to `/`. The client reads the account state when it loads, so there is no polling and no popup.

**Rationale**: a popup can be blocked and needs cross-window messaging; polling needs timers (AGENTS.md: no sleeps). Navigating away is acceptable because nothing is in progress when she signs in (she returns to her project, which the server restores, and reopens the document; this happens once per sign-in, and saving the open document across the navigation is left out as extra scope): summaries are discarded anyway (F003 FR-009), and the active project is restored from the server on load.

**Alternatives**: popup window with polling; new tab with a status poll.

## 3. First registration and the client identity

**Decision**: the first authorization request uses `client_id=dynamic_agent_client`, `agent_name_hint=LexScholar` and a generated `ext_agent_host_id` (`urn:uuid:` plus a random UUID). The callback returns the issued client id (it must be present on a new registration, else the sign-in is incomplete); it is stored with the credentials and used for every later request of that sign-in. Nothing is sent that identifies her or her machine.

**Sign-out deletes the issued client id and host id too** (spec FR-004 and constitution II: remove everything stored for the sign-in). The next sign-in registers again, which needs no approval (spike). The docs would keep them across sign-outs; the cost of deleting is that each sign-in may leave a connection entry in her ChatGPT settings that revocation does not remove. Accepted; if it proves annoying, keeping the two non-secret identifiers is a small later change that needs the owner's decision.

**Alternatives**: keep client id and host id after sign-out (the docs' advice, rejected for the spec's "everything").

## 4. Scopes, plan access and "no plan access"

**Decision**: request `openid profile email offline_access resource.invoke chatgpt.tokens.use.direct` with `resource=https://api.openai.com/v1`, PKCE (S256), fresh `state` and `nonce`. After the exchange, plan access exists only when the granted scope contains `chatgpt.tokens.use.direct`. If it does not, the credentials are discarded, tokens are revoked and the outcome is `no_plan_access` (FR-005). Plan usage may also be refused later with `403 subscription_sharing_user_not_eligible`; that is treated the same way: the stored sign-in is deleted and she is shown as signed out with the "no plan access" message (FR-005), with no loop through sign-in.

**Rationale**: the docs say a valid ID token alone does not authorize plan usage and tell apps to check the granted scope. Discarding is simpler than the docs' "keep the sign-in, mark plan usage off": the spec says a no-plan account is not treated as usable for summaries and sign-out must leave nothing.

**Alternatives**: keep an identity-only sign-in (more states, no use for it here); `prompt=consent` (only for upgrading an existing registration; not needed).

## 5. ID token

**Decision**: validate before saving: RS256 signature against the key set at `https://auth.openai.com/.well-known/jwks.json` (selected by `kid`), issuer `https://auth.openai.com`, audience equal to the issued client id, not expired, nonce equal. The validated `sub` is the account identity; `email` is shown as the account name (falling back to the name claim, then "your ChatGPT account"). Signature checking uses `System.Security.Cryptography.RSA` with the key's modulus and exponent; any other algorithm is rejected.

**Rationale**: the docs list these checks. No JWT package: one algorithm, one issuer, about forty lines, and no new dependency (constitution V).

**Alternatives**: a JWT library (new package for one token type); skipping the signature because the token comes straight from the token endpoint over TLS (permitted by OpenID Connect, but contradicts the docs).

**To confirm**: the live token's algorithm and the claim names for the email (section 12).

## 6. Storing the credentials

**Decision**: one JSON file `chatgpt-signin.json` in the data directory, written to a temporary file with mode 0600 then renamed (atomic), read on use, deleted on sign-out. It holds the fields in [data-model.md](data-model.md). The client never receives any of it. A separate empty marker file `chatgpt-welcome-shown` records that the one-time welcome was shown; it contains nothing about her and survives sign-out so the welcome is not repeated (FR-007).

**Rationale**: the docs ask for an owner-only atomic file and keep tokens out of browser storage, logs and source control. The data directory already holds her library and has the process lock. SQLite would add a migration and no protection (the database file has the same permissions).

**Alternatives**: SQLite row (migration, same protection); the operating system keyring (not reliably present on Linux desktops, and a new dependency); the settings file or environment (she would handle secrets).

**Limitation**: protection is file permissions only; anyone who can read her files can read the tokens. Documented in the quickstart in user terms.

## 7. Refresh and revocation

**Decision**: before a summary starts, refresh when the access token expires within 10 minutes (a summary takes a few minutes at most, well inside the token's hour): form POST to the token endpoint with `grant_type=refresh_token`, the issued client id, the stored refresh token and `resource=https://api.openai.com/v1` (no `scope`); replace access token, expiry, scopes and refresh token together and write the file before using the new token. One in-process lock (the data-directory lock guarantees one process) surrounds every change to the stored credentials: saving a new sign-in, sign-out and refresh. A refresh saves its result only if the record still exists with the same refresh token, so a sign-out that happens while a refresh is in flight stays a sign-out. Network errors during a refresh keep the record and show a retry message. Terminal refresh errors (`invalid_grant`, `invalid_refresh_token`, `token_expired`, `refresh_token_expired`, `refresh_token_invalidated`, `refresh_token_reused`) delete the file and report `SignedOut`. Network errors and 5xx leave the file and report the service as unreachable. Sign-out sends the revocation request (form POST: `token`, `token_type_hint=refresh_token`, client id) to the discovery document's `revocation_endpoint` with a short timeout; whether or not it succeeded, the file is deleted, and if revocation was not confirmed the message says she can disconnect LexScholar in her ChatGPT settings.

**Rationale**: matches the docs and the spike. The 30-day refresh token is replaced on every refresh, so the latest must be saved first.

**Alternatives**: refresh on a timer (needs background work, F004's scope); refresh only after a 401 (fails the first request after an hour).

## 8. The Responses client behind the existing `Complete` seam

**Decision**: `ResponsesApi.complete` has the same type as `ChatCompletions.complete` (messages, cancellation → text or `ServiceError`). It sends `POST https://api.openai.com/v1/responses` with `Authorization: Bearer <access token>` and `{ model, instructions, input, store: false, stream: true }`: system messages become `instructions`, the others become input items. No `temperature` or `max_output_tokens`. The event stream is read line by line under the existing 1 MB cap: `response.output_text.delta` text is collected; only `response.completed` is success; `response.failed` is mapped by its error code; `response.incomplete` or a stream that ends without `response.completed` is `UnusableOutput`. The 100-second per-call limit of F003 applies.

**Error mapping** (HTTP status or `response.failed` code): `subscription_sharing_usage_limit_exceeded` (429, also mid-stream) → `UsageLimitReached`; `subscription_sharing_user_not_eligible` (403) → `NoPlanAccess`; `subscription_sharing_invalid_user` (401) → `SignedOut` (credentials deleted); `subscription_sharing_usage_unavailable` and `subscription_sharing_user_unavailable` (503) → `ErrorStatus` (try again); anything else → `ErrorStatus (status, None)`, so the short service message is not shown unless it passes F003's sanitized `errorMessage`.

**Rationale**: `Summarize.summarize` already does chunking, combine, validation and progress over `Complete`; replacing the capability keeps all F003 behavior (FR-014) with no change to the workflow. `store: false` and `stream: true` are mandatory for this flow.

**Alternatives**: a second workflow for the Responses shape (duplicate logic); non-streaming (not allowed in this flow).

## 9. Model choice

**Decision**: `GET https://api.openai.com/v1/models` with her access token before each summary; keep entries whose `visibility` is `list` and use the first in the server's order. There is no setting to override it: choosing a model belongs to F004's settings screen. If the list is empty or unreadable the summary fails with the service-unusable message.

**Rationale**: the spike listed eight models and ran with the first; the docs say the list is the way to know what an account offers and that a listed model is not proof of entitlement (an actual request is). Spec FR-013: automatic choice, no picker, no override.

**Alternatives**: a hard-coded model (breaks when OpenAI retires it); cache for the sign-in (one more state; the call is cheap).

## 10. Destination choice and the confirmation, enforced by the server

**Decision**: the summary request body gains `destination` (`chatgpt` or `configured`). It may be omitted only when exactly one destination is available; with two available and none given the server answers `400 destination_required`. For `chatgpt` the server requires the sign-in record, and until the record's `confirmedAt` is set it answers `409 confirmation_required` (with `destination: chatgpt` and the account) and sends nothing; a request with `confirmSend: true` sets `confirmedAt` in the file, then proceeds. Later summaries in the same sign-in need no confirmation. Deleting the file (sign-out, ended sign-in) clears it. The `configured` destination keeps F003's rule unchanged. A new `GET /api/summary/destinations` tells the client what is available, so the choice is shown before she asks.

**Rationale**: F003 enforces its confirmation on the server; the same rule here means a client bug cannot send text. Keeping `confirmedAt` in the credential record ties it exactly to "per sign-in" with no extra state.

**Alternatives**: client-side memory of the confirmation (not enforceable); a separate preference store (more state to clean on sign-out).

## 11. Sign-out abandons a running summary

**Decision**: the host keeps a cancellation token for the current sign-in (created at sign-in, cancelled at sign-out or when the sign-in ends). A ChatGPT summary request links it with `RequestAborted`. When it fires, the request ends with a `failed` event (`signed_out`) and nothing more is sent (FR-004).

## 12. Not stated in the docs, confirmed in the first slices

| Unknown | Where it is settled |
|---|---|
| A first registration accepts the host's own port (section 1) | Manual check, quickstart step 1; fallback: listener on 1455 |
| The ID token's signing algorithm and the email claim | Slice 4 on the real token; sign-in fails closed on anything else |
| The `revocation_endpoint` URL | Read from the discovery document at sign-out; slice 6 |
| The exact error bodies for usage limit, disconnect and no plan | Fake service follows the code table; manual validation checks the real ones where reachable. A free-tier account is not available to the owner, so "no plan access" is verified with the fake only |
| Approved sign-in button assets and formats (`/siwc/website` was not read) | Slice 10 reads them and adds the approved logo; the button label is "Continue with ChatGPT" |
| The unsupported Responses fields list | Only the fields in section 8 are sent |

## 13. Wording from OpenAI's guidelines (used verbatim)

- Label near the summary request: "Using ChatGPT plan", with a "Manage usage" link to `https://chatgpt.com/settings/usage`.
- One-time welcome after the first sign-in: heading "You’re using your ChatGPT plan", body "Eligible usage in this app uses your ChatGPT plan. Manage usage in your ChatGPT settings.", action "Got it".
- Usage limit: heading "Usage limit reached", body "Review your plan or this app's limit in ChatGPT settings.", primary action "Manage usage". No secondary "Buy app credits" (LexScholar sells none).
- Sign-in button: "Continue with ChatGPT" with OpenAI's approved branding, as prominent as any other sign-in option (there is none).
- Signed out: shown as "signed out" with the same sign-in button (LexScholar's own wording; the docs only say to stop using the token and ask her to sign in again).

## 14. Constitution and F003 consistency

No new package. F003's `Summarize` workflow, validation and client behavior are reused; the only changes to existing code are the three new `ServiceError` cases, the `destination` field and the new failure codes in the summary endpoint and client.

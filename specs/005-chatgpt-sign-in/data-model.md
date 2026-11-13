# Data model: Sign in with ChatGPT (F005)

Only the credentials are stored. Summaries and documents are unchanged (F003, F001).

## Sign-in record: `chatgpt-signin.json` (data directory)

Owner-only file (mode 0600), written atomically, deleted on sign-out or when the sign-in ends. Never returned by any endpoint, never logged.

| Field | Meaning | Secret |
|---|---|---|
| `clientId` | the client id issued at registration (never `dynamic_agent_client`) | no |
| `hostId` | `urn:uuid:` identifier sent at registration | no |
| `subject` | validated account id from the ID token | no |
| `account` | display name (email) shown to her | no (personal) |
| `accessToken` | bearer token for the plan's requests, valid about one hour | yes |
| `refreshToken` | replaced on every refresh | yes |
| `idToken` | the validated ID token of the sign-in | yes |
| `expiresAt` | when the access token expires (UTC) | no |
| `scopes` | granted scopes; must include plan usage | no |
| `signedInAt` | when she signed in (UTC) | no |
| `confirmedAt` | when she confirmed sending text to her plan in this sign-in; absent until then | no |

Rules: all changes (save, refresh, delete) happen under one lock, and a refresh saves only if the record still exists with the same refresh token; exists only after a sign-in that passed ID-token validation and granted plan usage; one record at a time; refresh replaces `accessToken`, `refreshToken`, `expiresAt` and `scopes` together and is written before the new token is used; any terminal failure deletes the file.

## Welcome marker: `chatgpt-welcome-shown`

Empty file. Created when she dismisses the one-time welcome. Contains nothing about her; kept after sign-out so the welcome is not repeated (FR-007).

## Pending sign-in (memory only, never written)

`state`, `nonce`, code verifier, redirect address, host id, expiry (10 minutes). One at a time; a new start replaces the previous one; lost when the host stops (a half-finished sign-in leaves her signed out, as the edge case requires).

## States seen by the client

`SignedOut` (no record), `SignedIn account` (record present), plus the return message of the last attempt (`signed_in`, `cancelled`, `no_plan_access`, `failed`) shown once. "Ended" sign-ins (disconnected, expired, refused) are `SignedOut` with a one-time "signed out" message.

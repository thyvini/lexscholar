# S001 / $16g — Reading an explicitly linked private Google Doc

**Question and hypothesis.** Can LexScholar read a private Google Doc from its link using only `https://www.googleapis.com/auth/documents.readonly`? **Yes by Google's API contract; not yet verified with a private Doc.** The consenting account must already be able to read the document. A [Restricted link](https://support.google.com/drive/answer/7166529) identifies the file; it does not grant access.

## What was observed

On 2026-11-26, in an isolated Linux x86_64 worktree (Bash 5.3, curl 8.23, jq 1.8), the following anonymous check of Google's public API description passed. It requests **service metadata**; it supplies no document ID, API key, cookie or OAuth token.

```bash
set -o pipefail
curl -q -fsS --max-time 20 'https://docs.googleapis.com/$discovery/rest?version=v1' |
  jq -e --arg scope 'https://www.googleapis.com/auth/documents.readonly' '
    .resources.documents.methods as $m |
    {revision, method: $m.get.httpMethod, path: $m.get.path,
     requiredId: $m.get.parameters.documentId.required, response: $m.get.response["$ref"],
     methodsAcceptingReadOnly: [$m[] | select(.scopes | index($scope)) | .id],
     hasList: ($m | has("list"))} |
    select(.method == "GET" and .path == "v1/documents/{documentId}" and .requiredId and
           .response == "Document" and .methodsAcceptingReadOnly == ["docs.documents.get"] and .hasList == false)'
```

Revision `20261118`: among the Docs v1 document methods, only `get` accepted the scope, it required an ID and returned type `Document`; there was no `list` method. No consent flow, token endpoint or document endpoint was exercised.

## What Google documents

| Question | Documented behavior |
|---|---|
| Which file? | The [Docs URL](https://developers.google.com/workspace/docs/api/how-tos/overview) contains `/document/d/{documentId}/`. [`documents.get`](https://developers.google.com/workspace/docs/api/reference/rest/v1/documents/get) takes that ID directly. The [sensitive read-only scope](https://developers.google.com/workspace/docs/api/auth) means “See all your Google Docs documents.” A known ID for another readable Doc is within the grant, too. Google's per-file `drive.file` alternative permits edits to app-authorized files; it is not a per-file read-only grant. |
| How to authorize? | For a local Linux install, Google's [desktop OAuth flow](https://developers.google.com/identity/protocols/oauth2/native-app) uses the system browser, an authorization code and a loopback callback. Use PKCE `S256` and validate a fresh `state`. Exchange the code and verifier at `https://oauth2.googleapis.com/token`; check the granted scope before sending the access token in a Bearer header to the Docs API. Denied consent means no read. |
| What expires? | The access token's `expires_in` is in seconds. With the offline refresh token, POST `grant_type=refresh_token` to the same token endpoint to obtain a new access token without another consent prompt. Under [External / Testing](https://support.google.com/cloud/answer/15549945?hl=en), the test user's authorization and refresh token expire **seven days from consent**. Docs read is outside the [identity-only exception](https://developers.google.com/identity/protocols/oauth2#expiration). A refresh error `invalid_grant` calls for fresh authorization; it does not by itself prove why the grant ended. |
| How to disconnect? | Google's [revocation endpoint](https://developers.google.com/identity/protocols/oauth2/native-app#tokenrevoke) accepts a form POST to `https://oauth2.googleapis.com/revoke` with `token` set to the refresh token. Revocation covers that user's grants across the project's OAuth clients, not just this document. HTTP 200 acknowledges revocation; enforcement can lag. Deleting a local token alone does not revoke the grant at Google. |

## Recommendation and live verification

Use the pasted link only to extract the ID locally. Issue `GET https://docs.googleapis.com/v1/documents/{documentId}` for that exact ID on the user's read action. No Drive search/list, document writes or following links in the text. Explain the broader Google grant before consent. Reading into the local workspace is a separate decision from sending text to an AI service: name the text, recipient and purpose and obtain a separate opt-in before any send.

**Owner input needed:** authorize an owner-operated test with one disposable Google account, a dedicated **External / Testing** Cloud project (Docs API enabled, Desktop OAuth client, account listed as a test user), and one Restricted, native Google Doc containing a harmless marker visible to that account. Enter the client configuration and exact link **locally**. Approve the single scope, retaining the test grant for a week, and revoking a fresh grant in that isolated project. No passwords, codes, tokens, client configuration, links or document text in chat, logs or Git.

1. Record consent time in UTC and confirm the returned scope is exactly `documents.readonly` (full URI above). GET the fixture; check HTTP 200, matching ID and marker locally. After the reported access-token lifetime, verify that the old bearer no longer reads it, refresh, and repeat the same successful GET.
2. Leave the same Testing grant unrevoked, with no new consent or publishing-status change. Compare a successful refresh before day seven with a refresh after seven elapsed days. Record times and the sanitized outcome. Moving a local clock does not test Google's expiry.
3. After that run, consent afresh and confirm the fixture reads. Revoke this second grant, then check that refresh and the old bearer read are rejected after propagation. Stop local use and discard the tokens. Record only times, scope, statuses/error codes and marker match yes/no.

**Status: open.** Private access, refresh across access-token expiry, revocation enforcement and seven-day Testing expiry all await the owner-run experiment.

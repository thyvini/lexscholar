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

Use the pasted link only to extract the ID locally. For a document-wide read, issue `GET https://docs.googleapis.com/v1/documents/{documentId}?includeTabsContent=true` for that exact ID. Read each `documentTab` in the returned [`tabs` and `childTabs` tree](https://developers.google.com/workspace/docs/api/how-tos/tabs); a bare GET returns content from the first tab only. No Drive search/list, document writes or following links in the text.

Explain the broader Google grant before consent. Reading the linked Doc into the local workspace is a separate decision from sending its text to an AI service: identify which text would leave, the recipient and the purpose, and obtain a separate opt-in before any send.

### Sustainable authorization

Google documents the seven-day limit for External / Testing grants; reproducing it is not a delivery prerequisite. For a personal Google account, investigate an External **Published** desktop client: Google's [publishing-status guidance](https://developers.google.com/identity/protocols/oauth2/production-readiness/overview) describes an unverified-app warning and a lifetime 100-user cap for sensitive scopes, and strongly discourages unverified public apps. A verified client avoids that warning but requires Google's approval and may take longer. Do not assume either configuration or an exemption is approved or works for this account. If the researcher has an eligible Workspace organization, an **Internal** client is another documented option, restricted to that organization and subject to administrator policy. Testing with reauthorization every seven days is a short-term fallback, not a sustainable silent background grant. All configurations still require handling revocation and other [refresh-token invalidation](https://developers.google.com/identity/protocols/oauth2#expiration). Do not publish or alter an OAuth project without owner authorization.

### Owner-operated experiment

Use a **dedicated disposable project**: revocation removes the user's grants for every client in that project. The script is an experiment, not production OAuth or secure long-term token storage. It keeps credentials and document content in memory and never writes a token file; it prints only sanitized results. Run it in a private local terminal, not an agent session or recorded shell.

1. In Google Cloud, create a separate project; enable the Google Docs API. Under Google Auth Platform configure an External consent screen in Testing, add the test account as a test user, add the `https://www.googleapis.com/auth/documents.readonly` data scope, and create a **Desktop app** OAuth client. Download its JSON to a private directory outside this repository. This scope grants read access to **all** the account's Google Docs, though the script requests only the specified document.
2. Create a private native Google Doc with Restricted sharing, readable by the test account. Create two top-level tabs and one nested child tab. Put distinct harmless markers `S001_TOP_ONE`, `S001_TOP_TWO` and `S001_CHILD` in their respective bodies. Do not use research content. Copy its document ID (the part between `/document/d/` and the next `/`) locally.
3. In a private terminal, with Python 3 and network access:

   ```sh
   mkdir -p "$HOME/s001-private"
   chmod 700 "$HOME/s001-private"
   python3 -m venv "$HOME/s001-private/venv"
   "$HOME/s001-private/venv/bin/pip" install google-auth-oauthlib requests
   "$HOME/s001-private/venv/bin/python" scripts/spikes/s001-google-docs.py
   ```

   Enter the absolute client JSON path and document ID at the prompts, not in shell history or chat. The script opens the browser and uses Google's loopback flow. Consent as the authorized test account. It fetches only that ID, recursively visits child tabs, checks each marker in its expected position, refreshes immediately and repeats the read, then asks for explicit confirmation before revocation. It checks refresh rejection after revocation; a still-working token or network error is **inconclusive**, not a pass.
4. Share only the printed PASS/FAIL/INCONCLUSIVE lines and provider error category, with no account names, IDs, document content, credentials or browser URLs. Remove the downloaded client JSON and virtual environment when finished; revoke the test project grant in [Google account permissions](https://myaccount.google.com/permissions) if the script did not confirm revocation. A successful refresh is not proof of behavior after access-token expiry.

**Status: open.** Private access, recursive tab content, immediate refresh and revocation enforcement await the owner-run experiment. Seven-day expiration is documented provider behavior, not live evidence. No live Google account or Doc has been accessed.

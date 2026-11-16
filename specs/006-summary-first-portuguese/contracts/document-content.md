# Contract: document content (F006 change)

`GET /api/documents/{documentId}/content` (existing; `ReaderEndpoints.fs` renamed `DocumentEndpoints.fs`).

## Unchanged

- `200` with `application/pdf`, `Content-Disposition: inline`, range requests (`206`, `416`).
- For a request whose `Accept` doesn't include `text/html` (API callers, `fetch`): problem responses as before. `404` `document_not_found`, `503` `document_unavailable`, `application/problem+json`, English `title`/`detail`.

## New: navigation answer

When the request's `Accept` header includes `text/html` (a browser opening the link in a tab) and the document can't be served:

| Case | Status | Body |
|---|---|---|
| stored file missing, unreadable or wrong size | `503` | HTML page, title "Documento indisponível" |
| no such document | `404` | HTML page, title "Documento não encontrado" |

- `Content-Type: text/html; charset=utf-8`, `X-Content-Type-Options: nosniff`, `<html lang="pt-BR">`.
- Body text (exact): for 503, "O arquivo deste documento não foi encontrado ou não pode ser lido. O registro na biblioteca e os projetos não foram alterados."; for 404, "Este documento não está mais na biblioteca."
- No script, no file path, no code, no exception text.
- The library record and the associations are not changed (F001 FR-017).

The `PATCH /api/projects/{projectId}/documents/{documentId}` role change is unchanged.

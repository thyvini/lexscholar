# F002 export format v1

One ZIP (`application/zip`) with UTF-8 filenames, containing:

```text
README.txt
manifest.json
pdf/<document-uuid>.pdf
summaries/<project-uuid>/<document-uuid>.txt
```

Names use canonical UUIDs; no original filename is used as a path. Exactly one PDF entry per exported document and one text summary per stored project/document summary. `README.txt` in Brazilian Portuguese explains archive contents, generated/unverified summaries, 1-based PDF page positions and that in-app restoration is not provided.

`manifest.json` is UTF-8 JSON, with root `format: "lexscholar-library"`, integer `version: 1`, `documents`, `projects`, and `associations` arrays. UUIDs are canonical strings; timestamps UTC ISO-8601; arrays may be empty. Fields:

- document: `id`, `originalFileName`, `byteLength`, `pageCount`, `importedUtc`, `sha256` (recorded at import, not a new integrity assertion), `pdfPath`.
- project: `id`, `name`, `createdUtc`; projects are those referenced by exported associations, not a full workspace backup.
- association: `projectId`, `documentId`, `role` (`FocusedSource` or `ReferenceSource`), `addedUtc`, and `summary` (null or object).
- summary: `generated: true`, `reviewNotice: "Gerado para sua revisão. Não verificado."`, `generatedAt`, `serviceHost`, `model`, `pagesCovered`, `unreadablePages`, `omittedPoints`, `points` (array of `{text, pages}`) and `textPath`.

Each summary text includes the review notice, document name and ID, project name and ID, source PDF relative path, each point and its one-based cited page numbers, covered/unreadable pages, omitted count, generated timestamp, service and model. Points and citations exactly reflect the stored summary; do not generate new claims or citations. All references resolve within the archive and JSON/text use the same captured state. No credentials, raw instructions, settings, database file or filesystem storage key. The export is inspectable and portable, not a supported LexScholar restore archive.

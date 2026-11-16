# Quickstart: validate F006

Prerequisites: build as in AGENTS.md; browsers installed (`scripts/install-browsers.sh`); for steps 6–7 a ChatGPT Plus or Pro account.

## Automated

```sh
scripts/check.sh T001 T002 T003 T004 T005 T006 T007 T008 T009 T010 T011
```

All journeys run in Chromium and Firefox. CI runs the full suite.

## Manual, in Firefox and then Chromium

Run `dotnet run --project src/LexScholar.Server`, open http://127.0.0.1:5000, with a fresh data directory.

1. **First run (screen 05)**: the welcome shows the three steps in Portuguese. Create the project "Dissertação" and import `tests/Fixtures/summary/` PDFs. The project list shows "Projetos", "Biblioteca", "Fontes principais", "Fontes de referência".
2. **Summary first**: open a document. The main area shows "Resumir este documento" and "Resumir", and there is no page view. Sign in with "Continuar com o ChatGPT" and summarize. The confirmation reads "Enviar documentos ao ChatGPT?", and its link opens the pt-BR help article. Progress shows, then the summary with "Gerado para sua revisão. Não verificado." and the date as dd/mm/aaaa, hh:mm.
3. **Citations (SC-001)**: select every citation of the summary. Each opens a new tab with the same document at the cited page; for `pp. N–M` it opens at page N. Note the browser, the document and the pages checked.
4. **Abrir PDF**: a new tab opens the whole document; the summary stays in the app.
5. **Missing file (FR-007)**: stop the app, rename the document's stored file in the data directory's originals folder, and start it again. "Abrir PDF" and a citation open a tab with "Documento indisponível" in Portuguese; the document is still in the library and the project. Restore the file.
6. **Roles (FR-008)**: in the document list, change a document from Fonte principal to Fonte de referência; it moves section.
7. **Excluir (FR-006)**: "Excluir", then "Cancelar": the summary stays. "Excluir", then "Excluir": the request is shown again, also after a restart.
8. **Only ChatGPT (FR-005)**: with a summary service in the settings file, no screen offers or mentions it.
9. **No English (SC-003)**: walk every screen and provoke an import failure (a non-PDF file) and a signed-out failure. Every text is Portuguese.

Record the results in `evidence/` (T012, T013).

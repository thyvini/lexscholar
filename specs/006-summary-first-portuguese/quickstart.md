# Quickstart: validate F006

Prerequisites: build as in AGENTS.md; browsers installed (`scripts/install-browsers.sh`); for real-plan generation (including step 2, step 10 and the extension checks), a ChatGPT Plus or Pro account.

## Recorded validation

The owner confirmed that all F006 validation was performed and passed, including Firefox and Chromium, native PDF citations, feedback preservation and the real ChatGPT plan. This is **owner-attested manual validation**, not an agent-observed run or a new automated result. See [T012](evidence/t012-manual-validation.md) for the code-map audit/manual scope and [T013](evidence/t013-owner-smoke-check.md) for the real-plan attestation and evidence limits.

## Automated

T025 feedback replacement: `scripts/check.sh T025 T023 T024 T004 T008`.

T023 optional first-summary instruction: `scripts/check.sh T023`. Impacted regressions: `scripts/check.sh T023 T016 T020 T021 T007` (builds with warnings as errors).

```sh
scripts/check.sh T001 T002 T003 T004 T005 T006 T007 T008 T009 T010 T011
```

All journeys run in Chromium and Firefox. CI runs the full suite. These commands are automated checks, not evidence of real-model compliance or native PDF page rendering.

## Manual extension checks, in both browsers

### Regenerate with feedback (T025)

1. Open an existing summary. Enter optional feedback under "Como melhorar este resumo?" (2,000-character limit).
2. Choose "Refazer resumo". Even for a previously confirmed account, a fresh dialog discloses document text and feedback. Cancel: no request starts, the old summary and draft remain.
3. Confirm deliberately. The old summary stays visible while the replacement runs. Leave for the library and reopen the document: the old version remains and progress resumes.
4. On success, the complete validated new summary replaces the old one; PDF citation links still open validated pages. A failed attempt shows an error while retaining the old version.
5. Stop an active replacement: the old version remains. The client uses POST `/cancel`, not DELETE. No raw feedback is separately stored or logged; changing documents or reloading clears drafts. Ratings, point flags and Recentes/history remain out of scope.

### Optional instruction (T023)

1. Open a document without a summary. Check the "O que você quer desta fonte? (opcional)" field and its limit/privacy hint.
2. Leave it blank to keep default main points; on another unsummarized document, enter a focus such as "Compare os fundamentos e limites da tese."
3. On the first request after sign-in, check that confirmation discloses the optional instruction. Cancel: no generation starts and the draft remains editable. Edit and deliberately send.
4. Verify the generated summary remains in Portuguese with valid PDF page links. For a long document, the same focus is supplied to all map/combine stages (automated prompt capture; no real-model guarantee).
5. Changing project/document or reloading clears an unsent draft. No Recentes/history is introduced. Regeneration/feedback is the separate T025 extension above. The raw instruction is not separately saved; generated content can reflect it.

## Manual, in Firefox and then Chromium

Use a throwaway data directory, never your real workspace: create a new one at the start of each browser pass, so both start at the first-run screen, with `export LEXSCHOLAR_F006=$(mktemp -d /tmp/lexscholar-f006.XXXX)`; within a pass, start (and restart, in steps 5 and 7) with `LexScholar__DataDirectory=$LEXSCHOLAR_F006 dotnet run --project src/LexScholar.Server`, then open http://127.0.0.1:5000. Every step below, including moving a stored file in step 5, happens in that directory only.

1. **First run (screen 05)**: the focused welcome shows the three steps in Portuguese without sidebars. Create the project "Dissertação" and import `tests/Fixtures/summary/` PDFs; creation does not leave setup. Explicitly finish (or skip optional steps) to enter the workspace. The workspace then shows "Projetos", "Biblioteca", "Fontes principais", "Fontes de referência".
2. **Summary first**: open a document. The main area shows "Resumir este documento" and "Resumir", and there is no page view. Sign in with "Continuar com o ChatGPT" and summarize. The confirmation reads "Enviar documentos ao ChatGPT?", and its link opens the pt-BR help article. Progress shows, then the summary with "Gerado para sua revisão. Não verificado." and the date as dd/mm/aaaa, hh:mm.
3. **Citations (SC-001)**: select every citation of the summary. Each opens a new tab with the same document at the cited page; for `pp. N–M` it opens at page N. Note the browser, the document and the pages checked.
4. **Abrir PDF**: a new tab opens the whole document; the summary stays in the app.
5. **Missing file (FR-007)**: stop the app, move the document's stored file from the data directory's originals folder to a folder outside the data directory (a renamed file left in `originals` is moved to quarantine by startup cleanup), and start it again. "Abrir PDF" and a citation open a tab with "Documento indisponível" in Portuguese; the document is still in the library and the project. Stop the app, move the file back to its original name and place in `originals`, and start it again.
6. **Roles (FR-008)**: open a document by its name; activate its name again to reveal options. Change it from Fonte principal to Fonte de referência; it moves section.
7. **Excluir (FR-006)**: "Excluir", then "Cancelar": the summary stays. "Excluir", then "Excluir": the request is shown again, also after a restart.
7b. **Fechar**: on a failed summary, "Fechar" removes the failure at once, with no confirmation.
8. **Only ChatGPT for new requests (FR-005)**: with a summary service in the settings file, new requests offer only her ChatGPT plan, with no configured-service choice or settings-file explanation. Open a stored configured-service summary: its recorded host, model, date and existing context remain visible, and it never shows "Usando seu plano ChatGPT · Gerenciar uso". Historical provenance is the explicit exception to hiding configured-service UI.
9. **No English (SC-003)**: walk every screen and provoke an import failure (a non-PDF file) and a signed-out failure. Interface text is Portuguese; source file names, recorded provenance and historical summary content are preserved.
10. **New summary language (FR-016, $153)**: through the real ChatGPT plan, summarize English, Portuguese and mixed-language documents, including a long document that requires chunking. New summary points must be in Brazilian Portuguese with valid citations. Open a pre-existing English summary: its text and provenance must remain unchanged, also after restart. Fake-service instruction tests do not replace this model-compliance check (T013).

Record the results in `evidence/` (T012, T013).

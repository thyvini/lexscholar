# Owner checklist: current project and PDF workflow ($166)

The owner has approved this practical single-researcher verification standard.
This checklist has **not been run** and records no results. It checks the current
workflow separately in desktop Firefox and Chromium on Linux; it is not a
participant study or a statistical substitute for the original criteria.
F001 [T046](../../specs/001-research-workspace/tasks.md) remains unchecked and
deferred to beta, and the original 90% unassisted participant thresholds for
SC-001 and SC-006 remain unverified, not passed or waived. See the
[verification status](../../specs/001-research-workspace/verification.md#usability-evaluations-sc-001-sc-006)
and [workstream E note](workstream-e-governance-verification.md#166-owner-checkable-usability-evidence).

## Preparation

- Use the release revision to be assessed; record `git rev-parse HEAD`. Use
  installed desktop browsers and record their actual versions, not Playwright
  versions. Enable each browser's built-in PDF viewer; an external viewer or
  downloaded file alone does not verify browser page navigation.
- Use the two-page synthetic CC0 fixture
  [`tests/Fixtures/valid.pdf`](../../tests/Fixtures/valid.pdf), target **page 2**.
  Its identifier and hash are in the [fixture README](../../tests/Fixtures/README.md).
  No private research PDFs, sign-in or summary generation are needed.
- For **each browser**, start with a fresh throwaway directory, never the real
  workspace. From the repository root, start the app:

  ```sh
  export LEXSCHOLAR_CHECK=$(mktemp -d /tmp/lexscholar-166.XXXXXX)
  LexScholar__DataDirectory="$LEXSCHOLAR_CHECK" dotnet run --project src/LexScholar.Server
  ```

  Confirm startup reports that directory, then open `http://127.0.0.1:5000`.
  Stop the app before starting the next pass with a new directory. Finish or
  skip the optional onboarding steps to enter the workspace. Keep the test
  directory until evidence is recorded; handle its disposal separately.

## Perform all checks in each browser

Record each result independently using the template below. If a prerequisite
fails, mark dependent checks **not run** and explain why; do not infer a pass.

1. **Create and switch projects.** Create two distinctly named projects using
   **Criar projeto**. Select each project in **Projetos**, then switch back.
   After each selection, identify the active project from the interface and
   check that it is the project selected. Record what indicated it.
2. **Import.** With the first project selected, open **Biblioteca** / **Adicionar
   documentos**, import `valid.pdf`, and check that the library shows the file.
3. **Associate and switch.** Add the imported file to the first project as
   **fonte principal** using **Adicionar como fonte principal**. Check it
   appears in that project's source list. Switch to the second project and
   check that the association does not appear there; switch back and check
   that it remains in the first project.
4. **Open and navigate.** Select the associated source and choose **Abrir PDF**.
   Check that the PDF opens and renders in the browser's built-in viewer in a
   new tab. Navigate from page 1 to the specified page 2 and verify the viewer
   displays page 2. Record what you observed and any workaround or confusion.

## Evidence template

Copy this section into a dated evidence record (for example
`specs/001-research-workspace/evidence/owner-workflow-166-YYYY-MM-DD.md`).
Complete one table row per check **per browser** with `pass`, `fail`, or
`not run`, the actual observation and any assistance, confusion or workaround.
Do not fill in a result before performing the check. Link supporting evidence
if available, avoiding private document content and credentials.

```markdown
# $166 current-workflow owner check

- Owner/reviewer and date:
- App revision (`git rev-parse HEAD`):
- Linux distribution/version:
- Firefox version:
- Chromium version:
- PDF identifier, page count and hash:
- Specified target page:
- Workspace isolation / PDF viewer configuration:

| Browser | Check | Result (pass/fail/not run) | Actual observation, assistance, confusion or workaround | Evidence link |
|---|---|---|---|---|
| Firefox | Create and switch projects | | | |
| Firefox | Import | | | |
| Firefox | Associate and switch | | | |
| Firefox | Open and navigate | | | |
| Chromium | Create and switch projects | | | |
| Chromium | Import | | | |
| Chromium | Associate and switch | | | |
| Chromium | Open and navigate | | | |

Outstanding failures, not-run checks, and limitations:
Owner disposition / follow-up:

These are owner observations of the current workflow, not unassisted participant
evidence. T046 and the original SC-001/SC-006 thresholds remain deferred and
unverified; no participant success rate or formal acceptance is claimed.
```

Keep failures and incomplete checks visible and record follow-up rather than
reporting overall success. Record actual results before deciding the disposition
of $166; approval of this standard alone is not a result.

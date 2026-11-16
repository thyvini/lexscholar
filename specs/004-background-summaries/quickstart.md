# Quickstart: F004 Background Summaries

Service setup is unchanged from F003 (`specs/003-document-summary/quickstart.md`, "Configure a service") and F005 (sign in with ChatGPT).

## What is stored (user-facing)

- A finished summary is saved in the workspace database on this machine, for that document in that project, until you dismiss it or remove the document from the project.
- A summary in progress or a failure is kept only while LexScholar runs; closing the app loses it, and you can ask again.
- **Not yet available**: exporting stored summaries and deleting the whole workspace's data from the app. They come with F002 (`$12y`).

## Automated checks

```sh
scripts/check.sh <task IDs>          # per slice
```

CI runs the full suite, browsers included. Browser journeys use `FakeChatService`/`FakeOpenAi` with handshakes; no real service.

## Smoke check (owner, at T003)

On your ChatGPT plan: open a project document of a few pages, ask for a summary, confirm, open another document straight away, wait about a minute, return: the summary is shown and a citation opens its page. Record the result in T003's evidence.

## Manual validation (real setup, Firefox and Chromium)

| Check | Steps | Expected |
|---|---|---|
| SC-001 | 3 attempts: ask, confirm, switch document (twice) or project (once), return | Finished summary each time |
| SC-002 | Follow every citation of a background summary | Each opens the cited page |
| SC-003 | Cause a failure while elsewhere (usage limit or stop the local service) | Same message as when waiting |
| SC-004 | Throughout | Nothing shown on another document |
| SC-005 | Start a plan summary, open another document, sign out | No summary on return; no new plan request after the sign-out (a summary that finished first may stay, AR 9) |
| SC-006 | Switch documents with a job running | No perceptible slowdown |
| SC-007 | Finish a summary, close and reopen the app; then dismiss and reopen | Shown after restart; gone after dismiss |
| SC-008 | View another document while a summary runs | List marker shows running, then finished |

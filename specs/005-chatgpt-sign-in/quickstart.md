# Quickstart: Sign in with ChatGPT (F005)

How to run and validate the feature. Decisions: [research.md](research.md); endpoints: [contracts/chatgpt-api.yaml](contracts/chatgpt-api.yaml); stored data: [data-model.md](data-model.md).

## For the researcher (user terms)

1. Start LexScholar and open a document in a project.
2. Choose **Continue with ChatGPT** where you ask for a summary. Your browser opens ChatGPT's sign-in. Sign in and allow LexScholar to use your plan.
3. You return to LexScholar and see which account is signed in and a one-time note: "You’re using your ChatGPT plan".
4. Ask for a summary. The first time after each sign-in, LexScholar tells you that the document's text will leave your computer for your ChatGPT plan and asks you to confirm. Where you ask, it says **Using ChatGPT plan**.
5. **Sign out** removes the sign-in from your computer and asks OpenAI to revoke it. You can also disconnect LexScholar in ChatGPT's settings.

**Where things go and what is kept**: the document's text goes to OpenAI under your ChatGPT account; how it is used and kept follows your ChatGPT data controls (OpenAI's article "Using your ChatGPT plan in other apps and sites" explains them). LexScholar never sees your ChatGPT conversations. On your computer it keeps one file in the data directory with your sign-in, readable only by your user account; anyone who can read your files as you can use it, so sign out on a shared computer. Summaries are never saved. Needs a ChatGPT Plus or Pro plan. Usage counts against your plan; you review it and set a weekly limit for LexScholar in ChatGPT's usage settings.

If you also configured a service in the settings file (F003), you choose between the two where you ask.

## Validation

Prerequisites: .NET SDK from `global.json`, `scripts/install-browsers.sh` once for browser tests.

**Automated** (fake OpenAI service, no network):

```sh
scripts/check.sh T0xx [T0xx ...]     # task IDs from tasks.md
```

The fake covers sign-in (signed in, cancelled, no plan access, bad ID token), refresh and rotation, a terminal refresh failure, a disconnected sign-in, the usage limit (before and during a stream), model listing and the confirmation rules.

**Manual, with her real account** (the release gate, also completes F003's T012). Start a clean data directory:

```sh
LexScholar__DataDirectory=/tmp/lexscholar-f005 dotnet run --project src/LexScholar.Server
```

| # | Do | Expect |
|---|---|---|
| 1 | Sign in with ChatGPT in Firefox | Returns signed in with the right account |
| 2 | Look at the welcome | Shown once; after "Got it", not shown again, also after sign-out and sign-in |
| 3 | Summarize a short public-domain PDF | Confirmation names the ChatGPT plan; summary with citations; label "Using ChatGPT plan"; context names ChatGPT plan and the model |
| 4 | Summarize again | No confirmation; same label |
| 5 | Restart LexScholar | Still signed in |
| 6 | Sign out; list the data directory; search it and the logs for the account's email and for token text | No `chatgpt-signin.json`; no email or token anywhere (SC-002, SC-006) |
| 7 | Sign in again, then disconnect LexScholar in ChatGPT's settings, then ask for a summary | "Signed out" with a sign-in button; nothing technical |
| 8 | Set LexScholar's weekly limit very low in ChatGPT's usage settings, summarize a long document | "Usage limit reached" with **Manage usage** opening the usage settings |
| 9 | Repeat 1–4 in Chromium | Same |
| 10 | Summarize a 40-page document | Within F003's SC-007 time on her plan |

"No plan access" (free tier) is verified with the fake service only: no free account is available.

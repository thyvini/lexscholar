# T010: client destination, plan confirmation and failure messages

- RED (`scripts/check.sh T010`, commit "test(f005): ... RED"): 9 of 36 failed on assertions (destination not sent, no choice rule, plan confirmation, new failure wording, parsing of the new codes, request body).
- GREEN (`scripts/check.sh T010`): 36 total, 0 failed, 0 skipped.
- CI full suite: see the PR's `build-and-test` check.

Wording chosen where the task gave none:
- Signed out: "You are signed out of ChatGPT. Sign in again to summarize with your plan."
- No plan access: "This ChatGPT account's plan can't be used by apps, so you are signed out."
- Usage limit: "Usage limit reached. Review your plan or this app's limit in ChatGPT settings." with the primary action "Manage usage" (https://chatgpt.com/settings/usage).
- Choice needed: "Choose where to send the text, then try again."

Notes: a `signed_out` stream event, a `no_plan_access` event and the 503 `chatgpt_signed_out` problem are treated alike (sign-in offered, destinations reloaded). The view itself (radio choice, "Using ChatGPT plan" label, link targets) is covered by the T011 browser journey, not by state tests.

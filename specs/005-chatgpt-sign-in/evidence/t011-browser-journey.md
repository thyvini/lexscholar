# T011: browser journey with the fake service

Tests only; the behavior existed (T004-T010), so there is no failing-first commit. Sensitivity checks instead (uncommitted, restored):

- `usageLink` pointed at `https://chatgpt.com/` instead of the usage settings: `scripts/check.sh T011` failed, 2 of 20 (both browsers), on "Locator expected to have attribute 'href' 'https://chatgpt.com/settings/usage'".
- Label "Using ChatGPT plan." changed to "Using plan.": failed 2 of 20 on "expected to contain text 'Using ChatGPT plan'".
- GREEN (`scripts/check.sh T011`, repeated 4 times): 20 total, 0 failed, 0 skipped. With T004: 50 total, 0 failed.
- CI full suite: see the PR's `build-and-test` check.

Covered in Chromium and Firefox: signed-out offer ("Continue with ChatGPT", `data-oai-siwc-button`); authorization address intercepted (no real OpenAI), callback visited with its `state`; account shown; welcome once with "Manage usage" -> `https://chatgpt.com/settings/usage`, gone after "Got it" and a reload; `?chatgpt=` removed from the URL; confirmation names the plan and account, carries the FR-010 notice and help link, and nothing is sent before Send; "Using ChatGPT plan" and a cited summary; the second summary without a prompt (2 requests); usage limit shows "Manage usage" at the usage settings address and keeps the sign-in; sign out offers sign-in again and revokes once at OpenAI. F003's not-configured check now expects the sign-in offer.

Not covered here: the radio choice with two destinations (plan and a configured service). One host takes one outgoing handler, so both would need a composed handler; it stays covered by the T010 state tests.

Finding: the first run of the welcome check failed once because the dismissal is a fire-and-forget POST and the reload could beat it. The test now waits for that response. A real user reloading within milliseconds of "Got it" could see the welcome again (harmless; follow-up, not changed here).

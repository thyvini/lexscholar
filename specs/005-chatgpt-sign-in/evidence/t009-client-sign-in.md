# T009 client sign-in, account, sign-out, welcome

- RED: `scripts/check.sh T009` against a no-op `ChatGpt.update` stub: 54 total, 14 failed (assertions on effects and state, commit "test(f005): client sign-in state tests, RED").
- GREEN: `scripts/check.sh T009`: build with `-warnaserror` passes, 54 total, 0 failed, 0 skipped (the state tests of F005's T009 plus the other T009-tagged tests of earlier features).
- Full suite and browsers: CI on the PR.

Notes
- Button: label "Continue with ChatGPT", OpenAI's white logo on a dark button and the `data-oai-siwc-button` attribute, per `https://developers.openai.com/siwc/website`. Source and licence status are in `docs/agents/codebase.md`. The page publishes no colours or sizes; `app.css` gained a few lines (`.siwc-button`, banner borders), approved by the lead although not in the task's file list.
- The `?chatgpt=` value is read once at start and removed with `NavigateTo(..., replace = true)`. The rendering and navigation are exercised end to end by T011's journey, not here.

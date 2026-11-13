# Notes: how the summary feature reaches a language model

**Status: open discussion, nothing decided, nothing built.** Recorded on 2026-11-13 so the thread is not lost. These are notes, not a spec: nothing here is a requirement, and no feature has been started. The owner wants to think and decide later.

## What triggered this

F003 (document summary) is implemented on `release/0.2.0`. It talks to any OpenAI-compatible Chat Completions service, configured in `appsettings.json` (`LexScholar:Summary:Endpoint`, `Model`, and the key in the environment variable `LexScholar__Summary__ApiKey`).

While preparing the manual validation (T012), the owner tried to run it and found the setup heavy:

- Anthropic's OpenAI-compatible endpoint refused the request: the account had no API credits (a billing error, reported as HTTP 400). This led to T013 (the app now shows the service's own short error message).
- Gemini's free tier also did not work. The cause was not found (the exact error was not captured).
- A local model (Ollama) was the fallback. It is slow on CPU, and one run ended with the message "abandoned". The cause is not known yet.

## The product concern

The real user is the owner's wife, not a developer. The goal stated by the owner:

> Connect her GPT account (like third-party apps do) and use her own plan limits, without having to care about API keys, local models or other complicated things.

Possible narrowing the owner mentioned: sign-in with the OpenAI or Claude account, using the tokens or limits of the respective plan or tier.

## What is and is not known

Known:

- Today the app needs manual configuration of a service URL, a model name and (for cloud services) an API key. The URL and model can go in `appsettings.json` or in environment variables; the key is meant to be set only through the environment variable `LexScholar__Summary__ApiKey`. That is too complex for the intended user.
- Anything other than a service on this machine sends document text off the machine. Constitution principle II applies: explicit, informed opt-in with a clear explanation of what leaves the machine. F003 already asks before every summary for a service elsewhere, and the confirmation is bound to the host and model shown.

**Not verified** (do not treat as fact; research before building):

- Whether OpenAI or Anthropic currently offer a sanctioned sign-in flow (OAuth or similar) that lets a third-party app use a consumer plan's limits (ChatGPT subscription, Claude subscription). The lead's understanding is that subscription limits and API billing are separate, and that a sign-in with a ChatGPT account exists for OpenAI's own Codex tool but not as a general program for other apps. This could be out of date.
- Whether reusing another tool's sign-in tokens is allowed by the providers' terms. It would likely be fragile and could put the user's account at risk. Do not build on it without confirming the terms.
- Which providers would be in scope for a first version.

## Possible outcomes

1. **Settings screen in the app (smallest).** The researcher pastes one API key and picks a provider from a list, with no files and no environment variables. The key is stored locally. It still uses API billing, not a consumer plan. Fits constitution V (smallest coherent capability). Needs thought on where and how the key is stored safely (constitution II).
2. **Account sign-in feature.** Only if a provider offers a sanctioned way for third-party apps. A research task would come first (current provider docs and terms). The feature would need its own spec, plan and tasks.
3. **Leave F003 as it is** and write a short guide with exact steps for a non-technical user. Cheapest, but does not remove the key and URL steps.
4. **Combine:** a settings screen now (option 1), and account sign-in later if research finds a sanctioned route.

Whichever is chosen should be a new feature with its own number. F004 is already named in F003's research as "background work", so it would take the next free number.

## Open items from F003 that this does not replace

- The "abandoned" message in a long local-model run: check tomorrow whether it appeared without dismissing or switching document or project, how long it ran, which model and file. If it did, treat it as a defect.
- Decision on the fixed 100 s request timeout (it also bounds a slow local model on one chunk), once T012 results exist.
- The manual validation T012 itself, still waiting on the owner.
- Wording drift in plan.md and the T007 entry about the confirmation binding.

## Next step

None until the owner decides. When they do, the usual route applies: specify, plan and tasks for the chosen option, with a research step first if sign-in is involved.

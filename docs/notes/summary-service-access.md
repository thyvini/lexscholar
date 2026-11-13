# Notes: how the summary feature reaches a language model

**Status: researched, not decided, nothing built.** Recorded on 2026-11-13 so the thread is not lost; research added the same day (see "Research" below). These are notes, not a spec: nothing here is a requirement, and no feature has been started. The owner wants to think and decide later.

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

**Not verified when first recorded** (the Research section below answers the first two for OpenAI and Anthropic):

- Whether OpenAI or Anthropic currently offer a sanctioned sign-in flow (OAuth or similar) that lets a third-party app use a consumer plan's limits (ChatGPT subscription, Claude subscription). The lead's understanding is that subscription limits and API billing are separate, and that a sign-in with a ChatGPT account exists for OpenAI's own Codex tool but not as a general program for other apps. This could be out of date.
- Whether reusing another tool's sign-in tokens is allowed by the providers' terms. It would likely be fragile and could put the user's account at risk. Do not build on it without confirming the terms.
- Which providers would be in scope for a first version.

## Possible outcomes

1. **Settings screen in the app (smallest).** The researcher pastes one API key and picks a provider from a list, with no files and no environment variables. The key is stored locally. It still uses API billing, not a consumer plan. Fits constitution V (smallest coherent capability). Needs thought on where and how the key is stored safely (constitution II).
2. **Account sign-in feature.** Only if a provider offers a sanctioned way for third-party apps. A research task would come first (current provider docs and terms). The feature would need its own spec, plan and tasks.
3. **Leave F003 as it is** and write a short guide with exact steps for a non-technical user. Cheapest, but does not remove the key and URL steps.
4. **Combine:** a settings screen now (option 1), and account sign-in later if research finds a sanctioned route.

Whichever is chosen should be a new feature with its own number. F004 is already named in F003's research as "background work", so it would take the next free number.

## Owner's ranking (2026-11-13)

Weighted with Fibonacci numbers: outcome 2, account sign-in (8); outcome 4, settings screen now and sign-in later (3); outcome 3, guide (2); outcome 1, settings screen only (1). Sign-in first, so the research step comes first.

## Research (2026-11-13)

Sources are listed at the end. Primary documentation was read where it could be fetched; points marked *third-party* come from articles, not from the provider.

### OpenAI: "Sign in with ChatGPT" with plan usage exists

- Announced at OpenAI DevDay on 2026-09-29 (*third-party*). A third-party app can send model requests that count against the user's ChatGPT plan instead of API billing.
- **Eligibility:** OpenAI's docs say plan usage covers "open-source and locally hosted apps". Paid or remotely hosted apps use an interest form instead. The same docs also say plan usage "is available to all open-source partners and selected private clients", so whether an app like LexScholar qualifies without approval is **not certain**; only a real sign-in shows it.
- **Flow:** authorization code with PKCE and OpenID Connect, a loopback callback on `127.0.0.1`, first registration with `client_id=dynamic_agent_client`, no client secret or partner key. This fits LexScholar's loopback-only host.
- **Plans:** ChatGPT Plus and Pro. Free accounts can sign in but cannot use their plan in other apps.
- **Limits:** a weekly cap per app, set by the user as a share of the plan; on Plus, a five-hour limit shared across apps. Going over returns `429 subscription_sharing_usage_limit_exceeded` (also mid-stream).
- **API:** the Responses API (`/v1/responses`), not Chat Completions, which F003 uses. Requests must set `store: false` and `stream: true`. `temperature`, `max_output_tokens` and several other parameters are not supported. Available models are listed with the user's token (`GET /v1/models`).
- **Tokens:** access tokens last 1 hour; refresh tokens last 30 days and are replaced on each refresh (*third-party*), so the app must store the latest one.
- **Not found in the docs read:** data handling (training, retention) and terms or prohibited uses for plan usage. Read them before building.

### Anthropic: subscription sign-in in third-party apps is prohibited

- Anthropic's updated legal and compliance terms (reported in February 2026, *third-party*) prohibit using OAuth tokens from Claude Free, Pro and Max subscriptions in third-party products. Subscriptions work only in Claude Code and Claude.ai; third-party apps must use API keys.
- So LexScholar can support Claude only through an API key (outcome 1 or 4), never through her Claude subscription.

### What this means for LexScholar

- Outcome 2 is possible **for ChatGPT only**, and needs her ChatGPT Plus or Pro plan.
- It is a new client, not a setting: the Responses API with streaming, model listing, and the usage-limit states.
- It stores new data on her machine (refresh tokens), so constitution II applies: safe local storage, sign-out, and deletion of the stored credentials.
- Document text still leaves her machine. F003's per-summary confirmation (FR-014) still applies.
- A settings screen (F004) is the natural place for the sign-in button and for API keys for other providers.

### Proposed next steps (not decided)

1. A short spike: register and sign in through the loopback flow with her account, list the models, send one small request, and read OpenAI's terms and data handling for plan usage. Record the result here.
2. If it works: a new feature, F005 "Sign in with ChatGPT" (sign-in and sign-out, token storage, a Responses API client, model listing, usage-limit messages), with its own spec, plan and tasks, within the size budget.
3. F004 as planned, with the settings screen as the home for sign-in and API keys.

## Open items from F003 that this does not replace

- The "abandoned" message in a long local-model run: set aside for now (owner, 2026-11-13). It came from an error Ollama reported, and local models may need tuning before use; revisit when local models are set up properly.
- The 100-second timeouts still matter for larger documents, whatever the model: the client's `HttpClient` keeps .NET's default 100 s until the response headers arrive, and the server sends nothing before the first chunk is summarized; the server's call to the service has the same 100 s limit per chunk. A slow chunk can hit either. Possible fix: start the response at once (for example a `started` event with the page total), give the summary request no client timeout (dismissing already cancels it), and make the per-chunk limit a setting. Decide once T012 results exist.
- The manual validation T012 itself, still waiting on the owner.
- Wording drift in plan.md and the T007 entry about the confirmation binding.

## Next step

The owner decides whether to run the spike (Research, step 1). Until then nothing is built.

## Sources

- [Sign in with ChatGPT: open-source and locally hosted apps](https://developers.openai.com/siwc/token-sharing-open-source) (OpenAI)
- [Sign in with ChatGPT: full documentation](https://developers.openai.com/siwc/llms-full.txt) (OpenAI)
- [Registration and sign-in](https://developers.openai.com/siwc/token-sharing-open-source/sign-in) (OpenAI)
- [Using your ChatGPT plan in other apps and sites](https://help.openai.com/en/articles/20001542-using-your-chatgpt-plan-in-other-apps-and-sites) (OpenAI Help)
- [Sign in with ChatGPT for developers](https://dev.to/hassann/sign-in-with-chatgpt-for-developers-the-oauth-flow-plan-usage-and-what-it-means-for-your-api-bill-48f7) (DEV Community, third-party)
- [OpenAI makes "Sign in with ChatGPT" a way to use your subscription](https://thenewstack.io/sign-in-with-chatgpt/) (The New Stack, third-party)
- [Anthropic officially bans using subscription authentication for third-party Claude use](https://alternativeto.net/news/2026/2/anthropic-officially-bans-using-subscription-authentication-for-third-party-claude-use) (AlternativeTo, third-party)

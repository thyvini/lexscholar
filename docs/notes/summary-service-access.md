# Notes: how the summary feature reaches a language model

**Status: research and spike done (the spike passed); F005 "Sign in with ChatGPT" chosen, on `release/0.2.0`; nothing built yet.** Recorded on 2026-11-13 so the thread is not lost; research, the spike, the terms reading and the release decision were added the same day.

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

### Spike result (2026-11-13)

A throwaway Python script (standard library only, not committed) ran the documented flow on the owner's machine with a ChatGPT Plus or Pro account. Every step passed:

| Step | Result |
|---|---|
| Registration with `dynamic_agent_client`, loopback `127.0.0.1:1455` | OK: an issued client id (`oaiapp_…`), no approval or partner key needed |
| Token exchange | OK: `expires_in` 3600, refresh token present |
| ID token | OK: issuer `https://auth.openai.com`, audience is the issued client id, nonce matches (signature not verified in the spike) |
| Plan-usage scope | OK: `chatgpt.tokens.use.direct` granted, with `resource.invoke`, `offline_access`, `openid`, `profile`, `email` |
| Model list (`GET /v1/models`) | OK: 8 listed: `gpt-6.1-sol`, `gpt-6-astra`, `gpt-6.1-terra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna` |
| One request (`POST /v1/responses`, `store: false`, `stream: true`, `gpt-6.1-sol`) | OK: 3.7 s, 105 input and 32 output tokens; an accurate one-sentence summary of a public-domain paragraph, citing `[Page 1]` as asked |
| Token refresh (standard `refresh_token` grant at the token endpoint; not documented on the pages read) | OK: HTTP 200, the refresh token rotates |

What this settles: an app like LexScholar can self-serve plan usage, and the flow, model list, streaming request and refresh work as described. Still open before building: OpenAI's terms and data handling for plan usage; how the weekly per-app cap and Plus's five-hour limit hold up for a 100-page document (roughly 100k input tokens across chunks); and the exact refresh request, which should be confirmed in OpenAI's documentation.

### Terms, data handling and app requirements (2026-11-13)

From OpenAI's Sign in with ChatGPT documentation. There is no separate terms document for plan usage in those docs.

- **Data handling:** requests use `store: false`, and plan usage gives the app no access to the user's conversations, other account context or API key. **Training and retention for plan-usage requests are not stated** in the developer docs. OpenAI's help article on using a ChatGPT plan in other apps could not be fetched from here; the owner reads it, and her ChatGPT data controls, before the spec settles what FR-014's confirmation says about where the text goes and how it is used.
- **Her controls:** she reviews usage and sets a weekly cap per app in ChatGPT's usage settings, and disconnects the app under Login connections. Using credits beyond the plan is off by default; if automatic purchases are on, continued use can be charged (*third-party*). OpenAI does not notify the app when she disconnects it, so the app must handle a refused token as "signed out".
- **Requirements on the app** (UI/UX guidelines): approved OpenAI branding and comparable prominence for the sign-in button; **Using ChatGPT plan** shown near where the request is made whenever the plan is used; **Manage usage** as the primary action when a usage limit is reached; a one-time welcome confirmation after the first sign-in with plan usage. (The rule to show which of the app's paid plans include plan usage doesn't apply: LexScholar has none.)
- **Credentials:** keep tokens out of browser storage, source control, logs and support transcripts.
- **Refresh, now documented:** form-encoded POST to `https://auth.openai.com/api/accounts/oauth/token` with `grant_type=refresh_token`, the issued `client_id`, the refresh token and `resource=https://api.openai.com/v1`, omitting `scope`; replace the access token, expiry, scopes and rotating refresh token together. This is what the spike did.

### Release decision (2026-11-13)

- F005 "Sign in with ChatGPT" is built on `release/0.2.0`, so v0.2.0 ships F003 and F005 together. This is an exception to "one feature per minor version": v0.2.0 cannot ship usefully without a working service for the researcher.
- T012 (F003's manual validation) completes after F005, with her ChatGPT plan; v0.2.0 is released only after that.

### Proposed next steps

1. ~~A short spike~~: done, passed (see "Spike result"). Terms and app requirements: read (see above); training and retention: the owner reads OpenAI's help article.
2. Chosen: a new feature, F005 "Sign in with ChatGPT" (sign-in and sign-out, token storage, a Responses API client, model listing, usage-limit messages), with its own spec, plan and tasks, within the size budget.
3. F004 as planned, with the settings screen as the home for sign-in and API keys.

## Open items from F003 that this does not replace

- The "abandoned" message in a long local-model run: set aside for now (owner, 2026-11-13). It came from an error Ollama reported, and local models may need tuning before use; revisit when local models are set up properly.
- The 100-second timeouts still matter for larger documents, whatever the model. The server's call to the service has a 100 s limit per chunk, so any slow chunk fails as a timeout. The client's `HttpClient` keeps .NET's default 100 s until the response headers arrive: for a document split into several chunks the server sends a `progress` event before the first model call, so this only affects documents that fit in one chunk, where nothing is sent until that single call finishes. Possible fix: make the per-chunk limit a setting, and for one-chunk documents start the response at once (or give the summary request no client timeout, since dismissing already cancels it). Decide once T012 results exist.
- The manual validation T012 itself, still waiting on the owner.
- Wording drift in plan.md and the T007 entry about the confirmation binding.

## Next step

F005 "Sign in with ChatGPT" on `release/0.2.0`: the owner reads OpenAI's help article on plan usage in other apps (training and retention), then specify, clarify, plan and tasks as for F003.

## Sources

- [Sign in with ChatGPT: open-source and locally hosted apps](https://developers.openai.com/siwc/token-sharing-open-source) (OpenAI)
- [Sign in with ChatGPT: full documentation](https://developers.openai.com/siwc/llms-full.txt) (OpenAI)
- [Registration and sign-in](https://developers.openai.com/siwc/token-sharing-open-source/sign-in) (OpenAI)
- [Using your ChatGPT plan in other apps and sites](https://help.openai.com/en/articles/20001542-using-your-chatgpt-plan-in-other-apps-and-sites) (OpenAI Help; could not be fetched from the agent's environment)
- [Sign in with ChatGPT: the scope that spends your users' plan](https://workos.com/blog/sign-in-with-chatgpt-plan-usage-scope) (WorkOS, third-party)
- [Sign in with ChatGPT for developers](https://dev.to/hassann/sign-in-with-chatgpt-for-developers-the-oauth-flow-plan-usage-and-what-it-means-for-your-api-bill-48f7) (DEV Community, third-party)
- [OpenAI makes "Sign in with ChatGPT" a way to use your subscription](https://thenewstack.io/sign-in-with-chatgpt/) (The New Stack, third-party)
- [Anthropic officially bans using subscription authentication for third-party Claude use](https://alternativeto.net/news/2026/2/anthropic-officially-bans-using-subscription-authentication-for-third-party-claude-use) (AlternativeTo, third-party)

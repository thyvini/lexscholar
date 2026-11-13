# Feature Specification: Sign in with ChatGPT

**Feature Branch**: `docs/f005-chatgpt-sign-in` (spec work); implementation branches follow AGENTS.md

**Created**: 2026-11-13

**Status**: Draft

**Input**: User description: "Sign in with ChatGPT so summaries use the researcher's own ChatGPT plan." Background, research, the spike result and OpenAI's app requirements: `docs/notes/summary-service-access.md`.

## Clarifications

### Session 2026-11-13

- Q: When she is signed in with ChatGPT and the settings file also configures a summary service, which one does a summary use? → A: She chooses between them where she asks for a summary, whenever both exist.
- Q: How often is she asked to confirm before her document's text is sent? → A: For her ChatGPT plan, once per sign-in: the first summary after each sign-in asks, later ones do not until she signs out and signs in again. A settings-file service that is not on her machine still asks for every summary (F003 FR-014).
- Q: What does the confirmation tell her about how OpenAI handles her document's text? → A: That the text goes to OpenAI under her ChatGPT account, and that how it is used and kept follows her ChatGPT data controls, with a link to OpenAI's help article and a pointer to her ChatGPT data controls settings. It makes no claim of its own about training or retention.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in with ChatGPT and summarize with my own plan (Priority: P1)

The researcher signs in with her ChatGPT account from LexScholar, once, in her browser. From then on, summaries (F003) use her ChatGPT plan, with no service address, model name or key to set up. She can see which account is signed in and can sign out.

**Why this priority**: F003's service setup is too complex for the intended researcher, who has a ChatGPT plan and nothing else. Without this, summaries are unusable for her, and v0.2.0 ships F003 and F005 together.

**Independent Test**: With a ChatGPT account that has plan access, start with no summary service configured, sign in, open a document and request a summary. The summary appears with page citations, labelled as using her ChatGPT plan. Sign out and confirm that requesting a summary offers sign-in again.

**Acceptance Scenarios**:

1. **Given** the researcher is not signed in and no other summary service is configured, **When** she asks for a summary, **Then** she is offered the sign-in action (labelled as OpenAI's guidelines require) first, and the explanation of how to configure a service in the settings file is shown after it as secondary text.
2. **Given** she chooses to sign in, **When** she completes the ChatGPT sign-in in her browser and returns to LexScholar, **Then** the app shows her as signed in, shows which ChatGPT account, and she sees a one-time confirmation of what using her ChatGPT plan means.
3. **Given** she is signed in and has not yet confirmed since signing in, **When** she asks for a summary with her ChatGPT plan chosen, **Then** the place where she asks shows "Using ChatGPT plan", if this is the first summary since she signed in, she is told before any text is sent that the document's text will leave her machine for her ChatGPT plan, and nothing is sent unless she confirms. Later summaries in the same sign-in send without asking again.
4. **Given** she confirmed, **When** the summary is generated, **Then** all F003 behavior applies unchanged: progress as pages processed, the ability to dismiss, page citations, omitted points counted, and the "generated, for review" label. The model is chosen without her input, from the models her account offers.
5. **Given** she is signed in, **When** she signs out, **Then** the app shows her as signed out, everything LexScholar stored for the sign-in is gone, and requesting a summary offers sign-in again.
6. **Given** she is signed in, **When** she closes and reopens LexScholar, **Then** she is still signed in, without signing in again.
7. **Given** her ChatGPT account has no plan access (for example the free tier), **When** she signs in, or when a summary request finds it out later, **Then** she sees a clear message that her account's plan can't be used by LexScholar, she is shown as signed out with nothing stored, and no summary is attempted with it.
8. **Given** she is signed in, **When** the plan's usage limit is reached while asking for or generating a summary, **Then** she sees a clear message that the limit was reached, with "Manage usage" (her ChatGPT usage settings) as the main action, and no partial summary is presented as complete.
9. **Given** she disconnected LexScholar in her ChatGPT settings, or her sign-in expired, **When** she asks for a summary (or the app finds out otherwise), **Then** the app shows her as "signed out" with a way to sign in again, and nothing fails silently.
10. **Given** she is signed in with ChatGPT and the settings file also configures a summary service, **When** she asks for a summary, **Then** she chooses between her ChatGPT plan and the configured service where she asks, and the "Using ChatGPT plan" label, the confirmation and the context shown with the summary name the one she chose.
11. **Given** she started signing in, **When** she cancels in her browser, closes the tab or never returns, **Then** LexScholar stays signed out, shows no error that blames her, and she can try again.

---

### Edge Cases

- Signed in, but the settings-file service is the only one she wants: she chooses it each time she asks (whether the choice is remembered is a plan-level detail unless clarified); with no ChatGPT sign-in there is no choice to make.
- She confirms, then her sign-in ends and she signs in again (same or another account): the next ChatGPT summary asks for confirmation again.
- Sign-in is started twice, or from two tabs: only one sign-in can complete, and the app shows one consistent state.
- Her computer has no network, or ChatGPT can't be reached, during sign-in or a summary: a clear message that says what happened and that she can retry; she stays signed in if the sign-in itself was not refused.
- The usage limit is reached halfway through a long document: she is told the summary could not be completed, not shown a partial one as complete (F003's failure behavior applies).
- She signs out while a summary is being generated: the request is abandoned and its result is not shown (as when a document is closed, F003 FR-009).
- She signs in with a different ChatGPT account than before: the app shows only the new account; nothing of the earlier sign-in remains.
- Her sign-in can no longer be renewed (disconnected, expired, refused): shown as "signed out" at the next use, with sign-in offered again; stored sign-in data is removed.
- The app is stopped in the middle of sign-in: on restart she is signed out, not in a half-signed-in state.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The researcher MUST be able to sign in with her ChatGPT account from LexScholar by completing the sign-in in her browser, and MUST NOT have to enter a service address, model name or key.
- **FR-002**: When signed in, she MUST be able to have a summary use her ChatGPT plan, and the app MUST show which ChatGPT account is signed in. When she is signed in and the settings file also configures a service, she MUST choose between the two where she asks for a summary. When only one is available, it is used without a choice.
- **FR-003**: She MUST stay signed in across restarts until she signs out or the sign-in ends (FR-008).
- **FR-004**: The researcher MUST be able to sign out. Signing out MUST remove everything LexScholar stored for the sign-in, and a summary request in progress MUST be abandoned without showing its result.
- **FR-005**: When her ChatGPT account has no plan access, the app MUST say so clearly, MUST NOT offer her ChatGPT plan as a destination for summaries, and MUST NOT send any text. When this is only found out as a summary is requested, the stored sign-in MUST be removed and she MUST be shown as signed out with the same message.
- **FR-006**: Wherever she asks for a summary while her ChatGPT plan is the chosen destination, the app MUST show "Using ChatGPT plan".
- **FR-007**: After her first sign-in, the app MUST show a one-time confirmation of what using her ChatGPT plan means, and MUST NOT repeat it on later sign-ins by the same installation. (The record that it was shown contains nothing about her, so FR-004's removal does not include it.)
- **FR-008**: When her sign-in ends because she disconnected LexScholar in her ChatGPT settings, it expired or it was refused, the app MUST show her as "signed out", MUST offer signing in again, and MUST remove the stored sign-in data. No such case MAY fail silently or show as a generic error.
- **FR-009**: When her plan's usage limit is reached, the app MUST say so clearly and MUST offer "Manage usage", opening her ChatGPT usage settings, as the main action. The summary MUST NOT be shown as complete.
- **FR-010**: Before sending any document text to her ChatGPT plan for the first time in a sign-in, the app MUST tell her that the text will leave her machine and that her ChatGPT plan receives it, and MUST send nothing unless she confirms. Once she has confirmed, later summaries in the same sign-in MUST NOT ask again. Signing out (including when the sign-in ends, FR-008) MUST clear the confirmation, so the next sign-in asks again. A settings-file service that is not on her machine MUST still ask for every summary (F003 FR-014). The notice MUST say that the text goes to OpenAI under her ChatGPT account and that how it is used and kept follows her ChatGPT data controls, MUST link to OpenAI's help article "Using your ChatGPT plan in other apps and sites" and point to her ChatGPT data controls, and MUST NOT make its own claim about training or retention.
- **FR-016**: The confirmation for her ChatGPT plan (FR-010) MUST be distinct from the one-time welcome (FR-007): the welcome explains what using her plan means, and the confirmation is about her document's text leaving her machine.
- **FR-011**: LexScholar MUST NOT read or store her ChatGPT conversations, and MUST NOT store the document text or the summary beyond F003's rules.
- **FR-012**: Her sign-in credentials MUST NOT appear in the app's logs, error messages, the repository, or anywhere the browser page can read them.
- **FR-013**: The model used MUST be chosen automatically from the models her account offers. She MUST NOT be asked to choose a model in the app (choosing one belongs to F004's settings screen).
- **FR-014**: All F003 behavior MUST stay the same when summaries use her ChatGPT plan: page citations, omitted points, progress, dismissing, no stored summaries, and the "generated, for review" label. The context shown with a summary (F003 FR-008) names her ChatGPT plan as the service.
- **FR-015**: Cancelling or abandoning the sign-in MUST leave her signed out with no stored sign-in data, and she MUST be able to try again.

### Key Entities

- **ChatGPT sign-in**: whether the researcher is signed in with ChatGPT, which account, and what LexScholar keeps on her machine to stay signed in. Exists only after a successful sign-in; removed entirely on sign-out or when the sign-in ends. One per installation.
- **Plan usage state**: what the app can tell her about her plan when a request is made: available, limit reached, no plan access.
- **Summary service choice**: which service F003 uses for a summary (her ChatGPT plan, or a service in the settings file). When both are available she chooses at request time (see Clarifications).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A researcher with a ChatGPT plan and no other setup goes from first opening the app to a first summary, with no file, address, model or key involved, in under 5 minutes.
- **SC-002**: After signing out, a search of LexScholar's data directory and logs finds nothing from the sign-in: no tokens, no account identity.
- **SC-003**: In every scenario where her sign-in ends (disconnected, expired, refused, signed out in another tab), the next summary request shows "signed out" with a sign-in action, in 100% of the tested cases, and none shows a generic or technical error.
- **SC-004**: In 100% of tested cases where the plan is used, "Using ChatGPT plan" is visible where she asks for a summary, and no text is sent to her ChatGPT plan before she confirms for that sign-in.
- **SC-005**: Reaching the usage limit gives a message with a working "Manage usage" action in 100% of tested cases.
- **SC-006**: Across all tested flows, her sign-in credentials appear in no log line, error message, or committed file.
- **SC-007**: All F003 acceptance scenarios still pass when summaries use her ChatGPT plan.

## Assumptions

- The researcher has a ChatGPT Plus or Pro plan. Free accounts can sign in at ChatGPT but can't use their plan in other apps; that is the "no plan access" case.
- She signs in on the same machine that runs LexScholar, in her usual browser (Firefox or Chromium); the app is local and single-user.
- One ChatGPT account at a time per installation.
- OpenAI allows locally hosted apps like LexScholar to use plan usage without prior approval; the spike on 2026-11-13 confirmed this works.
- "Using ChatGPT plan", "Manage usage" and the one-time confirmation follow OpenAI's guidelines for apps; OpenAI's approved branding is used for the sign-in button.
- Her weekly cap and plan limits are set and shown in ChatGPT, not in LexScholar. Buying extra usage is not offered.
- A settings screen, long documents and background work are F004. Until then the sign-in controls live where she asks for a summary and in the reader's existing places, not in a settings screen.
- Out of scope: signing in with Claude (Anthropic's terms forbid using a Claude subscription in third-party apps; API keys only), choosing or overriding the model (a sensible default from her account's list is used), and buying extra usage.

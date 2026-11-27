# S003: daily reading email provider research

Research for [$16i](https://thyago-cunha-dev.codecks.io/card/16i), 2026-11-26. This is a documentation-only, provisional comparison for F009, not an implementation or completed delivery spike. No account, credentials, mailbox, or email was accessed; no message was sent. The existing local `s003-loopback.py` was not used or changed.

## Recommendation

For a Gmail account, investigate the Gmail API with the **sensitive** `https://www.googleapis.com/auth/gmail.send` scope first. It grants sending on the user's behalf without granting mailbox read access. Gmail SMTP with OAuth instead requests `https://mail.google.com/`, a **restricted** scope with read, compose, send, and permanent-delete access; Google says applications requesting it must demonstrate full utilization. That is disproportionate for send-only use. Generic SMTP remains a possible alternative for another provider, but its authorization, security, limits, and verification depend on that provider; app passwords are not inherently send-only.

This is not a provider decision or evidence of a working integration. Owner approval, provider configuration, and a real test remain necessary. Keep document-read authorization, email authorization, and consent for recurring AI processing distinct: granting one is not consent to either of the others.

## Provider details

| Concern | Gmail API | SMTP |
| --- | --- | --- |
| Authorization | `gmail.send` permits send-only; it is sensitive and public-app verification may apply. See [Gmail scopes](https://developers.google.com/workspace/gmail/api/auth/scopes). | Google's [XOAUTH2 documentation](https://developers.google.com/workspace/gmail/imap/xoauth2-protocol) specifies the full `mail.google.com` scope for Gmail SMTP. Other providers may differ. Google's [app password guidance](https://support.google.com/mail/answer/185833) describes a separate credential requiring 2-Step Verification and subject to account policy. |
| Submission | Construct an RFC-compliant MIME message, base64url encode it, and call `users.messages.send`; see [Google's sending guide](https://developers.google.com/workspace/gmail/api/guides/sending). | Submit MIME through an authenticated SMTP server using provider-required TLS and authentication. SMTP support and credentials must be verified for the chosen provider. |
| Refresh | Google's [desktop OAuth guide](https://developers.google.com/identity/protocols/oauth2/native-app) specifies system-browser authorization, PKCE, a loopback redirect, refresh tokens and reauthorization on invalid grant. Desktop clients cannot keep client secrets confidential. | OAuth SMTP requires provider-specific refresh; an app password is not an OAuth refresh token. |
| Operations | A Google Cloud project, enabled Gmail API and OAuth client are needed. Sensitive-scope verification, test-user restrictions, or Workspace administrator policy can impede use. See [scopes](https://developers.google.com/workspace/gmail/api/auth/scopes) and [desktop OAuth](https://developers.google.com/identity/protocols/oauth2/native-app). | Server settings, credential provisioning and administrative rules differ by provider. Do not assume Gmail's SMTP scope or limits apply elsewhere. |

Refresh tokens can be revoked or expire; long-running unattended use must surface reauthorization rather than claim ongoing delivery. Store credentials privately, never in logs; don't log message content or provider error bodies that might echo sensitive data. Google's desktop documentation also warns that programmatic revocation removes all OAuth scopes granted to a **project**, affecting other clients in that project; separation of consent in the UI alone does not establish independent revocation.

## Limits and uncertain outcomes

Google's [error guidance](https://developers.google.com/workspace/gmail/api/guides/handle-errors) says Gmail API sending limits are per user and shared across web, API and SMTP clients; 429 can last hours, 403 can indicate quota or domain policy, and 401 can require refresh or renewed consent. API request quotas are separate from sending limits; consult the current [Gmail API quotas](https://developers.google.com/workspace/gmail/api/guides/quotas) and applicable [Workspace sending limits](https://support.google.com/a/answer/166852) for the actual account. Google's [SMTP error list](https://support.google.com/mail/answer/3726730) includes too many recipients and daily sending limit exceeded. No quota is a delivery guarantee.

Google explicitly warns that quota enforcement can lag and **HTTP 200 does not prove an email was successfully sent**. Neither API acceptance nor an SMTP success response proves inbox delivery or reading. A timeout, connection loss after submission, or ambiguous server failure may mean the provider accepted the message even though the client lacks confirmation. Blind retry risks duplicates; a message ID alone is not a documented idempotency guarantee. With `gmail.send` alone the application cannot read the mailbox to reconcile uncertain sends. Google recommends backoff for certain errors, but that does not establish that every failed send is safe to repeat. F009 must choose and explain a duplicate-versus-missed-message policy, especially for ambiguous outcomes, before implementation.

When the local host is off or asleep, it cannot send on schedule. On restart, catch-up could skip missed days, send one current digest, or send a backlog; each has different duplication, quota, stale-content and consent consequences. The owner must choose behavior and timezone semantics; the spike does not assume a scheduler or promise a delivery time.

## Remaining validation and owner decisions

1. **Explicit owner authorization is required** before any account access or send, including a named approved test recipient, account/provider and test window. The present instruction prohibits both account access and email, so no such test was attempted.
2. With separate authorization, send one synthetic-data-only message to that recipient; check both plain-text and mobile-friendly HTML alternatives on an actual receiving client. Record observed result and limitations without publishing addresses, message contents or credentials.
3. Separately verify refresh and reauthorization, provider limits, loss of response and host-off catch-up behavior without assuming that a successful sample proves future delivery. Simulate ambiguous failures where possible; do not cause duplicate real sends without explicit approval.
4. Decide provider, consent boundaries, safe credential handling, uncertain-send policy and catch-up behavior before specifying production work. No real research material should be used in the spike.

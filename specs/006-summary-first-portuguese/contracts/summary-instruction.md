# Optional first-summary instruction (F006 T023)

Extends the existing F005/F004 `POST /api/projects/{projectId}/documents/{documentId}/summary` JSON object:

```json
{"destination":"chatgpt","confirmSend":true,"instruction":"Compare os fundamentos e limites da tese."}
```

- `instruction` is optional. If present it must be a string of at most 2,000 UTF-16 code units (the HTML/.NET length unit). Empty/whitespace means absent; surrounding whitespace is trimmed.
- Non-string (including null), overlong instruction, or a body beyond 16,384 bytes: `400 invalid_instruction`, no outgoing summary call. No input is reflected in the problem response. The cap accommodates a fully JSON-escaped maximum instruction. Legacy malformed/non-object/absent-body behavior is otherwise unchanged.
- Antiforgery, Host/Origin restrictions, destination and pre-send checks remain. An instruction never grants consent. Existing plan confirmation is still once per sign-in, not once per instruction. Confirmation text and the field disclose that the optional instruction goes with the document.
- Configured-service and plan paths forward it as a separate **user** message after source content, never as system/developer instructions or page markers. Single/map/every intermediate/final combine gets the same focus. Higher-priority rules retain Brazilian Portuguese, JSON, source-grounding and citations; actual citation parsing/omission remains unchanged.
- Existing running/finished summaries remain idempotent; this field does not regenerate or overwrite one. For a valid body, existing-summary retrieval still bypasses destination/consent/sign-in checks. Invalid instructions are rejected even when a summary exists.
- Response schemas/storage schema are unchanged. Raw instructions are not logged, returned as metadata, separately saved, or included in browser storage. The transient draft survives cancellation, clears on project/document change or job acceptance, and is lost on reload. A generated summary can of course reflect the focus or echo user text: this is not a guarantee that generated content cannot contain any instruction text.
- Regeneration/feedback, saved instruction metadata and recent-instruction shortcuts are not part of T023. The upcoming regeneration slice can reuse the optional field and shared workflow but must define its own consent/lifecycle/storage semantics; this contract adds no replacement operation.

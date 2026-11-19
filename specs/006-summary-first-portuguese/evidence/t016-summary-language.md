# T016 — New summaries in Brazilian Portuguese ($153)

- Owner-approved correction: all new summaries use Brazilian Portuguese regardless of source language. F006 FR-015/R11 explicitly supersede F003's source/majority-language rule and F006's former F007 deferral.
- RED: `LOG=/tmp/opencode/t016-red.log scripts/check.sh T016` — warning-as-error build passed; 6 tests, 4 failed assertions, 0 skipped. All three single-call source cases lacked the Brazilian-Portuguese instruction; the long-document instruction assertion also failed.
- GREEN: `LOG=/tmp/opencode/t016-green.log scripts/check.sh T016` — warning-as-error build passed; 7 tests, 0 failed, 0 skipped.
- Coverage: English, Portuguese and mixed-language single-call inputs retain their source text; a long English document exercises map, intermediate reduction and final combine instructions. Existing parser/citation rules remain unchanged. The existing server restart regression is additionally tagged T016: stored English `Main idea`, document id, host, model and generation date survive restart; GET and repeated POST make no new service request.
- Scope: three shared prompts only; both destinations consume them. No storage read/write changes, migration, translation of stored summaries, language detection or additional model call.
- Limitation: scripted replies demonstrate outgoing instructions and workflow behavior, not real model compliance. Quickstart step 10 adds real-plan output checks for T013; these were not performed here.
- Full-suite CI: pending the eventual PR; not run locally per AGENTS.md. No PR opened or merge performed for this slice.

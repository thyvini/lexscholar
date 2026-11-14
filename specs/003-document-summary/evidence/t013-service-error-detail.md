# T013: the service's own error message

Risk: untrusted service text reaches the researcher; GPT review expected.

## RED (commit "test(f003): the service's own error message reaches the researcher")
`scripts/check.sh T013` -> 23 total, 6 failed: details were the fixed generic/Model-Temperature text ("Sub-string not found" for the service's message, e.g. "Your credit balance is too low ...", "invalid x-api-key", capped message, key-echo case).

## GREEN
`scripts/check.sh T013 T010 T003` -> 83 total, 0 failed, 0 skipped.
- `ChatCompletions.errorMessage`: reads only `error.message` (string), replaces the API key, turns control characters into spaces, collapses spaces, caps at 300 characters; none if absent, non-string or blank.
- Error bodies are read under the same 1 MB cap and body timeout; over the cap or unreadable gives no message.
- `Summarize.failureDetail`: quotes the message; the Model/Temperature/MaxOutputTokens hint only for a 400 without one.
- Not logged: a test captures logs and finds neither the message, prompt nor key.
- The client already shows `detail` as plain text; it is unchanged.

## Limitations
- A string search in xunit ignores control characters under ICU, so the control-character check is per character.
- Full-suite CI run: see the PR.

## Greptile (2 x P2), both must fix, done
- A refusal body that breaks off or stalls became Unreachable/TimedOut (connection advice): now `ErrorStatus (status, None)`; the caller's cancellation still propagates. Test: broken body keeps the settings hint (failed before the fix).
- Blank message of non-breaking spaces or Unicode separators survived as a message: whitespace and format characters now count as spaces. Test added (failed before the fix). `scripts/check.sh T013 T010 T003` -> 85 total, 0 failed.

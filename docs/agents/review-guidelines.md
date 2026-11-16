# Review guidelines

What a reviewer checks on a LexScholar pull request. `scripts/gpt-review.sh` gives this file to GPT; any other reviewer (a person, Greptile's custom instructions) can use it too. AGENTS.md and `docs/agents/codebase.md` are the background.

## What to check, in this order

1. **Correctness, especially partial failures.** What happens if the process stops between two steps, or an operation is retried after such a stop? Startup must never be able to block the app permanently.
2. **Scope.** Does the PR do what its tasks ask and nothing more (constitution Principle V)? Extra safeguards, options or abstractions the spec doesn't ask for are findings too.
3. **Tests.** They check behavior, not implementation details; the RED failed on assertions; a test for behavior that already existed has a sensitivity check; no sleeps or polling for readiness.
4. **Conventions in AGENTS.md.** Dependency direction, host security untouched, functional-first F# as described below.

## F# style: functional-first, pragmatic

LexScholar is functional-first F# (patterns in `docs/agents/codebase.md`). Prefer the functional option when it is at least as clear; don't demand FP for its own sake. This is not a Haskell code base: no point-free puzzles, no monad transformers, no computation-expression gymnastics. Readability wins over cleverness.

Expected, and fine:

- Immutable records and unions; smart constructors returning `Result`; expected failures as `Result` or union cases, not exceptions; `Option`, never `null`.
- Workflows taking only the functions they need (function types or small records of functions), composed at the edge (`*Endpoints.fs`, `Program.fs`); no DI container, no service locator.
- Pure domain and application code; I/O only in Infrastructure and Server.
- Elmish `update` functions pure: `Model -> Message -> Model * Effect option`, effects as data.
- Exhaustive pattern matching over unions instead of boolean flags or string codes.

OOP is fine where the framework or interop requires it, so don't flag:

- ASP.NET Core hosting and middleware, EF Core `DbContext` and row types (mutable by design), Bolero components, `HttpMessageHandler`/`SocketsHttpHandler`, `IDisposable`/`IAsyncDisposable`, P/Invoke, `Task`-based interop, a local `mutable` inside a small loop.

Treat with caution, and suggest the functional alternative:

- Classes holding mutable state in Domain or Application.
- Interfaces with a single implementation, or abstractions without a second use (Principle V).
- Inheritance hierarchies, or Strategy/Visitor/Builder-style patterns where a function or a union fits.
- Exceptions used for expected outcomes, or swallowed exceptions.
- I/O, clocks or randomness inside Domain or Application instead of a passed-in capability.

## Rounds

A PR may be reviewed several times as it is fixed. Each round costs a push and a wait, so:

- **First review: everything at once.** Report every finding you verify in one pass, P2 included. Don't keep any for a later round.
- **Coverage:** report `COVERAGE: complete` only after reviewing the entire required scope, or `COVERAGE: incomplete` with what remains unread. An incomplete review cannot approve. Only a complete review may establish the next incremental baseline; missing coverage markers (including older reviews), incomplete coverage or a non-ancestor commit require a full review.
- **Later rounds: what changed.** Read the incremental diff and previous findings; open the full diff or other files only as needed for context. Check whether each finding of the last review is fixed, and repeat only the open ones. New findings must come from the changes since the last reviewed commit (introduced or exposed by them, in any file). Text the last review already read is out of scope, unless the problem is P0.
- A P0/P1 the owner has answered in a PR comment stays open until it is fixed or the owner decides; repeat it as it was, without arguing it again.

## Severity

- **P0**: data loss or a security problem.
- **P1**: must be fixed before merging: a bug, a broken contract or test, a scope violation, a failure that is hidden.
- **P2**: worth fixing, doesn't block.

A clearly OOP choice with a simpler functional fit is P2, with the alternative in one or two lines. Raise it to P1 only when it breaks the dependency direction (Domain ← Application ← Infrastructure ← Server), puts I/O in Domain or Application, or hides a failure. Don't report pure style preferences, and report only what you verified in the code.

# Proposed decision: functional-first F# architecture

**Status**: Proposed — awaiting owner approval.

**Date**: 2026-11-08

**Scope**: F001 implementation architecture; no feature or Constitution changes.

This is a separate proposal, not an amendment to the approved `plan.md` or
`research.md`. The T045 refactor follows the requested implementation direction;
adopting the following text into those approved documents requires approval.

## Proposed text for `plan.md` → Architecture and Project Structure

Use a functional core with effectful adapters at the boundaries. Model domain
state with immutable records and discriminated unions, and domain rules with
pure module functions. Application workflows compose those rules with explicitly
passed effectful functions. Group related capabilities into small immutable
function records when needed; use a function directly for a single capability.
Pass only the dependencies a function needs rather than a global application
environment or a service locator. Service classes and application interfaces are
not the default abstraction.

Use built-in computation expressions such as `task { ... }` to sequence effects
and manage resource lifetimes when needed. Pass cancellation explicitly. Do not
introduce a generic workflow framework, repository hierarchy, or dependency
injection framework. Framework-required classes, interfaces, and mutable objects
(ASP.NET Core hosting, EF Core contexts, streams, and disposable test hosts) stay
at the outer boundaries. Compose application functions explicitly at the host
boundary; framework DI remains available only where framework integration needs
it, not as the application composition model.

Preserve the five application projects and dependency direction: Domain owns pure
rules; Application owns use cases and narrow capability types; Infrastructure
supplies effectful adapters; Server composes them and maps HTTP; Client remains
the Bolero UI. The existing storage/content boundaries remain independent of the
viewer. This decision does not change the approved data model, HTTP contract,
storage technology, deployment, or feature behavior.

## Rationale and trade-offs

- Explicit functions make dependencies visible and replaceable in tests without
  service mocks, container registrations, or runtime service resolution.
- Immutable data and pure rules keep domain reasoning independent of HTTP,
  persistence, and file lifetimes.
- Small function records provide typed effect boundaries without adding layers.
  Split them only when callers need different capabilities; avoid an all-purpose
  dependency record.
- Composition and resource ownership must be explicit. Existing framework APIs
  remain object-oriented where required; this is not a ban on .NET interop.

## Follow-up document edits if approved

- Insert the proposed text into `plan.md` under Architecture and Project Structure.
- Clarify that “interfaces” in the plan's simplicity check and storage/content
  boundaries means narrow capability contracts, normally F# functions/records,
  not a requirement for CLR service interfaces.
- Record the decision and rationale in `research.md` if desired. Leave the
  Constitution unchanged.

## T045 boundary

Only scaffold representations change now. Application operations, PDF validation,
import journaling, and verified content remain explicit `NotImplementedException`
placeholders; HTTP routes remain reachable `501` placeholders. Configuration and
exclusive locking remain unimplemented functions. The existing disposable test
harness still owns the real loopback host, temporary SQLite file, and filesystem
root. No domain behavior, schema, T005–T011 implementation, or UI is added.

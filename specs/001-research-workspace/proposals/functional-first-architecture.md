# Architecture decision: functional-first F# architecture

**Status**: Approved and incorporated.

**Proposed / approved / incorporated**: 2026-11-08

**Scope**: F001 implementation architecture; no feature or Constitution changes.

The project owner approved adoption with a pragmatic clarification: prefer
functional-first F#, but use ordinary functions, records of functions, interfaces,
or classes wherever each is appropriate. Do not enforce purity dogmatically.

The decision is incorporated in:

- [Plan: Functional-first F# design](../plan.md#functional-first-f-design),
  including the storage capability-boundary clarification and unchanged
  five-project structure.
- [Research: Functional-first F# architecture](../research.md#functional-first-f-architecture),
  including rationale, trade-offs, and the architectural references.

Those planning sections are the current guidance. The original proposal below
is retained for provenance; its more restrictive wording about interfaces,
classes, and DI is qualified by the approved guidance above. This adoption is
documentation-only and does not implement or complete any task.

## Original proposed text (historical)

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

## Original rationale and trade-offs (historical)

- Explicit functions make dependencies visible and replaceable in tests without
  service mocks, container registrations, or runtime service resolution.
- Immutable data and pure rules keep domain reasoning independent of HTTP,
  persistence, and file lifetimes.
- Small function records provide typed effect boundaries without adding layers.
  Split them only when callers need different capabilities; avoid an all-purpose
  dependency record.
- Composition and resource ownership must be explicit. Existing framework APIs
  remain object-oriented where required; this is not a ban on .NET interop.

## Incorporation record

- Incorporated the decision into `plan.md`, with explicit allowance for ordinary
  functions, records of functions, interfaces, classes, and beneficial OO interop.
- Clarified that storage/content interfaces describe narrow capability boundaries,
  not necessarily CLR interfaces, in the plan and research.
- Recorded the rationale, alternatives, trade-offs, and references in `research.md`.
- Left the Constitution, product requirements, HTTP contracts, application code,
  and task completion statuses unchanged.

## T045 boundary (historical proposal scope)

Only scaffold representations change now. Application operations, PDF validation,
import journaling, and verified content remain explicit `NotImplementedException`
placeholders; HTTP routes remain reachable `501` placeholders. Configuration and
exclusive locking remain unimplemented functions. The existing disposable test
harness still owns the real loopback host, temporary SQLite file, and filesystem
root. No domain behavior, schema, T005–T011 implementation, or UI is added.

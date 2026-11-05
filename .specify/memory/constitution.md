# LexScholar Constitution

## Core Principles

### I. Academic Integrity and Verifiable Evidence
Research assistance MUST distinguish source statements from interpretation and generated synthesis. Summaries and answers that make factual claims about supplied material MUST provide traceable references to the supporting document and location when available. Generated claims MUST clearly disclose when their supporting source references are absent, ambiguous, or cannot be verified. The application MUST communicate when evidence is missing, ambiguous, or conflicting, and MUST NOT fabricate citations or present unsupported claims as source-grounded conclusions. This protects scholarly verification and responsible use.

### II. Privacy and User Control
Storage and processing of user research materials MUST occur locally by default. Any external storage or processing MUST require the user's informed, explicit opt-in, with a clear explanation of what data will leave the local environment and for what purpose. The application MUST protect user-provided materials and associated derived data against unauthorized access, loss, and unintended alteration. Users MUST be able to understand where their research materials are stored and how they are used. Data handling MUST be documented in user-facing terms.

Users MUST have meaningful controls to inspect, export, and remove user-provided materials and associated derived data. These controls MAY be delivered across separate approved features. Each feature that defers any of these capabilities MUST explicitly identify and track the missing controls, transparently document for users and reviewers what remains unavailable, and MUST NOT represent deferred functionality as implemented. Design decisions MUST NOT unnecessarily block future delivery of these controls. Deferral MUST NOT be represented as full compliance with this principle; the obligation remains outstanding until the controls are available. These rules preserve control over sensitive research while allowing reviewable increments.

### III. Human Oversight of Generated Content
AI-generated summaries, explanations, and answers MUST be presented as assistance for researcher review, not as authoritative scholarship. Users MUST be able to inspect relevant source material alongside generated claims and decide whether to retain, revise, or disregard generated content. The application MUST not silently represent generated content as user-authored or verified fact.

### IV. Testability and Reproducibility
Requirements MUST define observable acceptance criteria for behavior and quality. Changes affecting document interpretation, citations, or generated results MUST be verifiable against representative inputs, including cases where evidence is absent or uncertain. Where results depend on variable processing, the system MUST disclose enough relevant context for users and reviewers to understand and reproduce the process to a reasonable degree. Verification evidence MUST be recorded with the change.

### V. Simplicity and Incremental Development
Work MUST deliver the smallest coherent capability that satisfies an approved requirement. Each change MUST have a clear user need and avoid unrelated scope. New complexity MUST be justified in the relevant design artifact by a requirement or a demonstrated limitation. Development MUST proceed in reviewable increments.

### VI. Requirements and Implementation Decisions
Specifications MUST state user needs, expected behavior, constraints, and acceptance criteria without prematurely prescribing implementation choices. Technology and architecture choices MUST be recorded in planning artifacts with rationale and relevant tradeoffs. Constitution-level principles govern outcomes and constraints; they MUST remain technology-agnostic and MUST NOT mandate particular languages, frameworks, databases, AI models, agents, or architectural patterns.

## Development Workflow

LexScholar development MUST follow Spec-Driven Development. Feature work MUST be grounded in an approved specification before planning and implementation. Plans MUST preserve the distinction between requirements and implementation decisions, and tasks MUST be traceable to the specification and plan. Reviews MUST assess compliance with this constitution and record how applicable acceptance criteria were verified.

### Current user-control delivery tracking

F001 retains shared documents after their final project association is removed. Full-library export and deletion are deferred from F001 and tracked for F002 in [Codecks card $12y](https://thyago-cunha-dev.codecks.io/card/12y). Until the missing controls are delivered and verified, Principle II remains outstanding; this tracking does not establish full compliance. F001's approved feature scope remains unchanged.

## Governance

This constitution governs project specifications, plans, implementation, and reviews. Amendments MUST be proposed as explicit changes to this document, reviewed for impact on existing principles and artifacts, and recorded with an updated version and amendment date. Versioning follows semantic versioning: MAJOR for incompatible governance changes, MINOR for added principles or materially expanded obligations, and PATCH for clarifications that do not change obligations. Compliance MUST be reviewed during specification, planning, and change review; any exception MUST be documented with its rationale and scope in the relevant project artifact. Technology selection remains a planning decision and MUST comply with this constitution.

**Version**: 2.0.0 | **Ratified**: 2026-11-04 | **Last Amended**: 2026-11-04

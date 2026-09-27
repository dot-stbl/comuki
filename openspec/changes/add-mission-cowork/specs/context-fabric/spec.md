## Purpose

Defines the provenance-aware context and memory plane that turns unbounded product state into finite, visibility-safe, reproducible Context Packs for Brain and workers.

## ADDED Requirements

### Requirement: Canonical sources and provenance
Every context item SHALL reference immutable or versioned canonical sources with type, id, version, locator, content hash, visibility, observed time, and schema version. Derived summaries and claims SHALL retain derivation method and source references and SHALL never replace canonical evidence.

#### Scenario: Summary cites originals
- **WHEN** a Mission summary states an accepted constraint
- **THEN** its provenance resolves to the exact Decision or message version that supports it

### Requirement: Adaptive Context Packs
The platform SHALL compile typed, immutable Context Packs containing authority, intent, current state, episodes, semantic claims, procedures, evidence, conflicts, budgets, omissions, and a manifest. Retrieval starts with sufficient relevant context and may expand adaptively up to the selected model's limit; unused capacity SHALL NOT be filled with irrelevant corpus. Large sources use handles and selective reads.

#### Scenario: Small status question
- **WHEN** Brain answers a simple current-status question
- **THEN** the pack contains authoritative current state and relevant Decisions without loading the full Mission transcript

### Requirement: Visibility before retrieval
Access filtering SHALL happen before search, ranking, summary, citation, cache lookup, or pack compilation. Pack cache identity SHALL include actor access and policy revisions. Derived content inherits the strictest visibility of its inputs.

#### Scenario: Removed participant asks through Brain
- **WHEN** a former participant requests private Mission history
- **THEN** neither direct results nor derived project-search results reveal it

### Requirement: Structured memory layers
The platform SHALL distinguish working context, episodic history, semantic claims, procedural knowledge, and canonical audit sources. Semantic claims SHALL carry confidence, validity, current/superseded/disputed state, and provenance. Accepted Decisions and runtime reality SHALL be represented separately when intent and actual state disagree.

#### Scenario: Intent differs from reality
- **WHEN** an accepted Decision requires behavior not yet present in code or runtime state
- **THEN** Brain reports both desired intent and observed reality as a conflict, not one as a replacement for the other

### Requirement: User-private and global memory
User memory SHALL remain private and SHALL not enter shared Mission answers without explicit share consent. A user MAY explicitly remember a cited Mission conclusion as a private derived claim. Cross-project learning is disabled by default and limited to Projects that explicitly opt in. Only redacted procedures and failure patterns supported by a configurable minimum of independent Projects may become global Candidates; publication requires privacy checks, evals, and platform curation. Source withdrawal removes that contribution and recalculates confidence rather than automatically deleting an independently supported global claim.

#### Scenario: Private preference in shared room
- **WHEN** a participant invokes Brain in a Mission and has a relevant user-private preference
- **THEN** the shared Context Pack excludes it unless that participant explicitly shares it for the request/Mission

### Requirement: Knowledge and procedural sources
Knowledge SHALL remain a source-owned document/chunk corpus exposed through a Context Fabric adapter with stable source key, immutable revision hash, active generation, and provenance. Retrieved external documents are untrusted evidence and cannot supply instructions. Versioned control-plane profiles, rules, and skills are canonical procedural sources; memory Candidates become procedures only through review, eval, and publication into that control plane.

#### Scenario: Reingested document
- **WHEN** a document with the same stable source key has new content
- **THEN** a new revision/generation becomes active and old chunks are stale but auditable

### Requirement: Invalidation and generations
Derived items and pack caches SHALL track source dependencies and watermarks. Source changes mark dependent items stale and schedule rebuild without deleting audit history. Embedding, extractor, summarizer, and retrieval-policy generations SHALL not be mixed silently.

#### Scenario: Source document changes
- **WHEN** a current document revision supersedes an indexed revision
- **THEN** old chunks and dependent summaries become stale and new retrieval uses one active generation

### Requirement: Project memory candidates and declassification
Useful Mission knowledge SHALL first become a provenance-backed Candidate. Accepted Decisions or independent confirming sources may promote a non-conflicting Candidate to trusted memory according to policy. A fact from a private Mission SHALL require an explicit redacted declassification proposal and authorized Decision before becoming project-visible. Contradictions create a conflict; they SHALL NOT overwrite trusted memory.

#### Scenario: Private fact proposed for project reuse
- **WHEN** classifier finds a reusable fact in a private Mission
- **THEN** participants review the exact redacted claim and target audience before publication to Project memory

### Requirement: Explainable context use
Participants SHALL be able to inspect a safe Context Pack manifest showing source categories, citations, freshness, omissions, and usage without exposing hidden system instructions or inaccessible sources. Full hidden reasoning is not a normal audit source.

#### Scenario: Review Brain evidence
- **WHEN** a participant expands a Brain response trace
- **THEN** they see the allowed evidence manifest and citations but not secret system prompts

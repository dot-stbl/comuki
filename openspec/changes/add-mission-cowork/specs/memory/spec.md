## ADDED Requirements

### Requirement: Project memory candidate state
Automatically extracted reusable knowledge SHALL enter Project memory as a Candidate with structured claim, confidence, visibility, source references, extractor generation, and status. Accepted Decisions may establish trusted claims immediately; otherwise policy may require independent confirming provenance. Candidates SHALL NOT masquerade as trusted truth.

#### Scenario: Repeated independent evidence
- **WHEN** the same non-conflicting claim is supported by the configured number of independent sources
- **THEN** policy may promote it to trusted Project memory while preserving all provenance

### Requirement: Memory conflict state
A new claim that contradicts trusted memory SHALL create a conflict containing both claims and evidence. Retrieval and Brain answers SHALL surface the dispute; recency or model confidence alone SHALL NOT overwrite trusted memory.

#### Scenario: New evidence contradicts decision
- **WHEN** an extracted claim contradicts a trusted accepted Decision
- **THEN** both remain visible as intent/evidence conflict until resolved by policy or a new Decision

### Requirement: Private Mission declassification
Content derived from a private Mission SHALL remain Mission-visible. Publishing a reusable claim to Project memory requires a redacted declassification proposal that shows the exact future claim, provenance disclosure, and target audience and is approved under Mission and project policy.

#### Scenario: Declassification rejected
- **WHEN** participants reject publication of a Mission-derived claim
- **THEN** it remains available only within Mission-scoped memory and cannot appear in project digests

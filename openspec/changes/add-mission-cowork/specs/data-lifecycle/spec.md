## Purpose

Defines class-aware retention, holds, evidence preservation, encryption-key destruction, and auditable deletion across Missions, Tasks, Brain operations, memory, and artifacts.

## ADDED Requirements

### Requirement: Class-aware retention policy
Retention SHALL be evaluated as a policy function over resource class, scope, terminal time, pins, holds, and deployment profile. The conservative self-hosted default keeps Mission/chat/operations for 365 days after terminal state, audit/Decisions for seven years, all Mission artifacts until Mission deletion, diagnostic reasoning for seven days, and no durable presence/typing.

#### Scenario: Resource becomes eligible
- **WHEN** a terminal Mission passes its configured period without a hold
- **THEN** it becomes deletion-eligible but is not destroyed until an explicit authorized delete or configured automation Decision

### Requirement: Crypto-shred with minimal audit
Deletion SHALL destroy per-object data keys and remove bodies, artifacts, embeddings, summaries, caches, and derived indexes while retaining minimal audit metadata: ids, actors, timestamps, reason, hashes, and deletion facts. Encrypted objects SHALL use random UUIDv7 object ids rather than plaintext-hash paths; plaintext hashes support integrity and deduplication only within one visibility/lifecycle scope. Equal content across private scopes SHALL retain independent ciphertext and keys. Backups may retain encrypted blocks until rotation but SHALL become unreadable after key destruction.

#### Scenario: Deleted message in backup
- **WHEN** a message body is crypto-shredded while an older backup still contains ciphertext
- **THEN** restoring the backup cannot recover plaintext without the destroyed object key

#### Scenario: Equal files in different private Missions
- **WHEN** two private Missions store identical plaintext content
- **THEN** each receives an independent object id, ciphertext, and wrapped data key and deleting either Mission does not affect the other

### Requirement: Mission artifact preservation
All artifacts belonging to a Mission SHALL remain preserved while the Mission exists. Mission crypto-shred removes artifact objects and derived data together. Active Missions and Tasks with Run history cannot be deleted; they must first reach terminal state and satisfy impact, retention, hold, and approval rules.

#### Scenario: Delete active Mission
- **WHEN** an actor requests deletion of an Active Mission
- **THEN** deletion is denied and the actor is directed to an impact-aware cancel/complete flow

### Requirement: Incident and legal hold
A platform owner MAY place a scoped hold with reason, actor, created time, and mandatory review time. Holds SHALL block deletion without widening read access. They do not expire automatically; overdue review emits critical attention until extended or released.

#### Scenario: Retention passes under hold
- **WHEN** content reaches its deletion date while a hold is active
- **THEN** no crypto-shred occurs and the hold review remains visible to authorized operators

### Requirement: Diagnostic reasoning retention
Full hidden reasoning capture SHALL be disabled by default and enabled only by deployment configuration with project allowlist, encryption, redaction, audit, and default seven-day TTL. Captured reasoning SHALL never be promoted to memory or Context Packs.

#### Scenario: Diagnostic TTL expires
- **WHEN** diagnostic reasoning reaches its TTL without hold
- **THEN** its body and derived indexes are crypto-shredded automatically

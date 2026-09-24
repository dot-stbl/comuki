## ADDED Requirements

### Requirement: Personal chat remains distinct
Subject-owned personal ChatSessions SHALL remain private conversations separate from Mission rooms. Bare `comuki` continues to open personal chat; Mission collaboration uses Mission routes and `comuki mission <id>`. A personal session MAY propose creating or linking work but does not acquire participant membership semantics.

#### Scenario: Existing personal session after upgrade
- **WHEN** a user opens a pre-existing personal ChatSession
- **THEN** ownership, transcript, and resume behavior remain private to that subject

### Requirement: Chat uses shared capability and Brain operations
Personal chat actions SHALL use the same Capability Broker and durable Brain-operation contracts as Mission chat. `chat:use` grants conversation access only and SHALL NOT imply downstream capability permissions. Approval SHALL address a durable proposal/operation id and current policy rather than an implicit checkpoint alone.

#### Scenario: Member cannot approve plan without permission
- **WHEN** a user with `chat:use` but without the required plan/run capability approves a chat proposal
- **THEN** the effect is denied even though the user owns the ChatSession

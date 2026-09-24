## Purpose

Defines secure Project-scoped outbound webhook subscriptions for Mission attention and optionally consented full-content events.

## ADDED Requirements

### Requirement: Scoped signed subscription
A project administrator SHALL create a subscription with destination policy, event allowlist, optional Mission allowlist, payload class, version, signing secret reference, and active state. Deliveries SHALL be timestamped and HMAC-signed with replay-resistant ids. Runtime secrets remain opaque.

#### Scenario: Receiver verifies delivery
- **WHEN** Comuki sends a subscribed event
- **THEN** the receiver can verify timestamp, delivery id, schema version, and HMAC without receiving the secret from Comuki

### Requirement: Full-content consent and redaction
Safe metadata SHALL be the default payload. Message/proposal bodies and artifact ids require project-admin configuration plus Mission owner consent. Sensitive fields are redacted before signing; plaintext secrets never leave Comuki. Artifact references are stable ids, not transferable signed URLs.

#### Scenario: Owner revokes consent
- **WHEN** Mission owner consent is revoked
- **THEN** unsent and retrying full-content deliveries are cancelled and crypto-shredded while minimal delivery audit remains

### Requirement: Safe destination policy
Webhook destinations SHALL pass configurable scheme/host/CIDR allowlists and protections against loopback, link-local, metadata endpoints, unsafe redirects, and DNS rebinding. Operators MAY explicitly allow internal destinations.

#### Scenario: Metadata endpoint target
- **WHEN** a subscription resolves to a cloud metadata or link-local address without explicit allowance
- **THEN** validation rejects the destination

### Requirement: At-least-once ordered delivery
Webhook delivery SHALL use durable outbox semantics with exponential retries, receiver dedupe ids, dead-letter visibility, and manual replay by project integration operators. Ordering is maintained per subscription and Mission; a poison event dead-letters after bounded retries and releases the partition with an explicit gap audit.

#### Scenario: Receiver times out after commit
- **WHEN** receiver processes a delivery but Comuki misses the response
- **THEN** retry uses the same logical delivery id and the receiver can deduplicate it

### Requirement: Versioned envelopes and loop prevention
Payloads SHALL use a stable envelope plus per-event major schema version. Subscriptions pin supported majors. Correlation/causation chains, hop limits, own-origin exclusion, and duplicate fingerprints SHALL prevent webhook/service-account feedback loops.

#### Scenario: Event returns to origin
- **WHEN** a webhook consumer posts an event derived from the same causation chain back to Comuki
- **THEN** loop policy suppresses or quarantines it before unbounded fan-out

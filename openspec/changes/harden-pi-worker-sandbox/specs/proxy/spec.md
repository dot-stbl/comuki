## ADDED Requirements

### Requirement: Runtime mint of a virtual key
The proxy SHALL mint a virtual key at claim time bound to a project, an optional work-item id, allowed models, and an expiry. Minted keys SHALL be stored in the same lookup the proxy already uses for config-seeded keys. Config-seeded keys remain valid; mint does not replace them.

#### Scenario: Claim produces a live key
- **WHEN** a work item is claimed
- **THEN** the proxy accepts Bearer of the minted token for that project's upstream until expiry or revoke

### Requirement: Revoke on execution end
Complete, fail, lease-lost, and explicit revoke SHALL make the minted token fail authentication. Expiry alone SHALL also fail authentication.

#### Scenario: Revoked token is rejected
- **WHEN** the work item completes and a later request presents the minted token
- **THEN** the proxy rejects it as an invalid virtual key

### Requirement: Minted key is a capability not an upstream secret
A minted token SHALL authorize only the proxy. The proxy continues to swap it for the upstream key on the outbound hop. Workers and journals SHALL never see the upstream key.

#### Scenario: Upstream key stays in the proxy
- **WHEN** pi calls the stamped `ANTHROPIC_BASE_URL` with the minted token
- **THEN** the upstream request carries the provider key from proxy configuration, not the minted token

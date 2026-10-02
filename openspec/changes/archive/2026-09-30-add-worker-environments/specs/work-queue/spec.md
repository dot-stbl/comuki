## MODIFIED Requirements

### Requirement: Claim with SKIP LOCKED and label match
A claim SHALL atomically move the oldest `Queued` work item matching ALL of the worker's labels — `profile_key`, **`env_class`**, `profiles_ref` — to `Running`, stamping `leased_by`, `lease_until`, `heartbeat_at`, incrementing `attempt`, and returning the item's `id`, `run_id`, `profile_key`, `env_class`, `brief`, `lease_until` and `attempt`. The claim subselect SHALL use `FOR UPDATE SKIP LOCKED` ordered by `created_at` (FIFO within a profile) so concurrent claimers never block or double-claim. A work item with empty or unconfirmed `env_class` SHALL NOT match any claimer.

#### Scenario: Two workers claim concurrently
- **WHEN** two workers with identical labels claim at the same moment
- **THEN** each receives a different item and neither waits on the other's locked row

#### Scenario: Label mismatch is a miss
- **WHEN** the only queued item has a different env class or profiles ref
- **THEN** the claim returns no item (a value, not an error)

#### Scenario: Unconfirmed class is not claimable
- **WHEN** a queued item has profile and profiles_ref matching the worker but `env_class` is empty
- **THEN** the claim returns no item

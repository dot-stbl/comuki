## REMOVED Requirements

### Requirement: Anonymous webhook ingress
**Reason**: Intake is retired before first release; ingress moves to the `integrations` capability under the same `/api/hooks/{provider}/{key}` route with Integrations-owned handlers. Pre-release hard rename, no compatibility alias.
**Migration**: See `integrations` capability, requirement "Anonymous webhook ingress".

### Requirement: Supported providers
**Reason**: Intake is retired before first release; provider support moves to the `integrations` capability.
**Migration**: See `integrations` capability, requirement "Supported providers".

### Requirement: Yandex Tracker and Jira providers
**Reason**: Intake is retired before first release; provider behavior moves to the `integrations` capability.
**Migration**: See `integrations` capability, requirement "Yandex Tracker and Jira providers".

### Requirement: Source connections
**Reason**: Intake is retired before first release; source connections move to the `integrations` capability under `/api/v1/integration/sources`.
**Migration**: See `integrations` capability, requirement "Source connections".

### Requirement: Inbound ticket kind discriminator
**Reason**: Intake is retired before first release; the discriminator moves to the `integrations` capability, renamed `InboundItemKind`.
**Migration**: See `integrations` capability, requirement "Inbound item kind discriminator".

### Requirement: Pull-request / merge-request ingress (issue #27)
**Reason**: Intake is retired before first release; PR/MR ingress moves to the `integrations` capability.
**Migration**: See `integrations` capability, requirement "Pull-request / merge-request ingress (issue #27)".

### Requirement: Catalog fetch can opt into pull requests
**Reason**: Intake is retired before first release; catalog behavior moves to the `integrations` capability.
**Migration**: See `integrations` capability, requirement "Catalog fetch can opt into pull requests".

### Requirement: Profile routing (issue #27)
**Reason**: Intake is retired before first release; profile routing moves to the `integrations` capability.
**Migration**: See `integrations` capability, requirement "Profile routing (issue #27)".

### Requirement: Sync-back for PRs is a single issue-comment only
**Reason**: Intake is retired before first release; PR sync-back moves to the `integrations` capability.
**Migration**: See `integrations` capability, requirement "Sync-back for PRs is a single issue-comment only".

### Requirement: Admission rules
**Reason**: Intake is retired before first release; admission rules move to the `integrations` capability under `/api/v1/integration/admission-rules`.
**Migration**: See `integrations` capability, requirement "Admission rules".

### Requirement: Inbox and claim
**Reason**: Intake is retired before first release; the inbox/claim loop moves to the `integrations` capability under `/api/v1/integration/inbox*`. Run-based claim behavior is unchanged here — Task-based claim is a later, separate change (`add-work-management`, issue #89).
**Migration**: See `integrations` capability, requirement "Inbox and claim".

### Requirement: Native tickets
**Reason**: Intake is retired before first release; native item creation moves to the `integrations` capability under `/api/v1/integration/items`, renamed "Native inbound items".
**Migration**: See `integrations` capability, requirement "Native inbound items".

### Requirement: Sync-back outbox
**Reason**: Intake is retired before first release; the sync-back outbox moves to the `integrations` capability.
**Migration**: See `integrations` capability, requirement "Sync-back outbox".

### Requirement: Persistence layout
**Reason**: Intake is retired before first release; persistence moves to the `integrations` schema with a fresh squashed migration baseline (`__comuki_integrations`), not a carried migration history.
**Migration**: See `integrations` capability, requirement "Persistence layout".

### Requirement: Dashboard filter for the PR-review profile
**Reason**: Intake is retired before first release; the dashboard filter behavior moves to the `integrations` capability (dashboard consumers unchanged in structure, only renamed generated types).
**Migration**: See `integrations` capability, requirement "Dashboard filter for the PR-review profile".

### Requirement: Source connection schema (issues #38, #39)
**Reason**: Intake is retired before first release; source connection schema moves to the `integrations` capability under `/api/v1/integration/sources`.
**Migration**: See `integrations` capability, requirement "Source connection schema (issues #38, #39)".

### Requirement: Secret env var must resolve at write time (issues #38, #39, #40)
**Reason**: Intake is retired before first release; this behavior moves to the `integrations` capability with renamed error codes.
**Migration**: See `integrations` capability, requirement "Secret env var must resolve at write time (issues #38, #39, #40)".

### Requirement: Admission rules as sibling rows (issue #40)
**Reason**: Intake is retired before first release; this behavior moves to the `integrations` capability under `/api/v1/integration/admission-rules` and `/api/v1/integration/sources/{sourceId}/rules/{ruleId}`.
**Migration**: See `integrations` capability, requirement "Admission rules as sibling rows (issue #40)".

### Requirement: Source probe — draft and stored (issues #41, #42)
**Reason**: Intake is retired before first release; source probing moves to the `integrations` capability under `/api/v1/integration/sources/probe` and `/api/v1/integration/sources/{id}/probe`.
**Migration**: See `integrations` capability, requirement "Source probe — draft and stored (issues #41, #42)".
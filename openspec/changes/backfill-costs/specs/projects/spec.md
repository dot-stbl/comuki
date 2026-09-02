## MODIFIED Requirements

### Requirement: Settings shape
Per-project settings SHALL cover scale quotas (`minIdle`, `maxConcurrent`,
`idleTtlSeconds` with null meaning "engine default"), the approval gate
(`approveRequired`), the opt-in feature flags (`knowledgeEnabled`,
`verifyEnabled`, `proxyEnabled`), and budget caps (`softBudgetUsdMicros`,
`hardBudgetUsdMicros` with null meaning unlimited; 1 USD = 1_000_000 micros).
One settings row per project, created with it. Soft exceedance is advisory;
hard exceedance cancels attributed runs via the costs budget gate.

#### Scenario: Fresh project defaults
- **WHEN** a project is created
- **THEN** its settings row exists with the approval gate off, every feature
  flag off, unlimited soft/hard budgets (null), and the engine-default idle TTL

#### Scenario: Budget caps stored
- **WHEN** settings are updated with soft and hard USD micros
- **THEN** subsequent costs/budget reads observe the new caps without restart

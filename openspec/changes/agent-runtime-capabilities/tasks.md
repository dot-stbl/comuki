## 1. Phase A — Memory gap close

- [ ] 1.1 Add optional `ScopeKind` / `SubjectId` to `BrainRequest`
  in `Comuki.Shared.Contracts/Brain/` and verify the wire builds
  with `dotnet build comuki.slnx -c Debug`.
- [ ] 1.2 In `BrainAgent.RunAsync`, call `IMemoryStore.SearchAsync`
  with the new scope before the model loop and prepend the result
  to the existing `MemoryDigest`; verify the existing global-only
  call still returns identical content via unit test
  `BrainAgentTests.WhenScopeIsNull_DigestMatchesLegacy`.
- [ ] 1.3 Add a dashboard route `/memory` that lists visible facts
  per scope, grouped by `topicKey`, with a `forget` action gated by
  `memory:write`; verify the page renders with `bun run typecheck &&
  bun run lint && bun run test` exit 0.
- [ ] 1.4 Register `/memory` in the chat slash catalog with a `list`
  / `forget <id>` / `clear <topicKey>` argument parser; verify a
  scripted chat turn against `shared/api/mock/chat.seed.ts` shows
  the slash output.

## 2. Phase B — Discovery v0

- [ ] 2.1 Add `discovery.scan` MCP tool in
  `platform/src/host/Comuki.Host/Mcp/McpServer.cs` accepting
  `{ projectId, mode, brief? }`; verify it returns a `discoveryRunId`
  via the existing MCP integration test.
- [ ] 2.2 Wire `discovery.scan` to start a `WorkerRun` with profile
  `explore-readonly`, `reason = discovery.scan`, and a `brief`
  constrained to the chosen mode (quick / secrets / / integrations /
  full); verify the worker pool picks the run up and the existing
  scale policy respects `MaxConcurrent`.
- [ ] 2.3 Add a discovery report parser that reads
  `/run/output/discovery.md` and writes one `MemoryFact` per finding
  with `scope = project, subjectId = projectId, kind = standing,
  source = run`; re-runs SHALL supersede by `topicKey` per the
  existing rule. Verify with a unit test that re-running the same
  discovery writes a new fact and stamps `SupersededAt` on the old
  one.
- [ ] 2.4 Register `/discover [mode]` slash command; verify the
  chat turn against `chat.seed.ts` shows the run id and links to
  the runs dashboard.
- [ ] 2.5 Add `discovery:run` permission key in `Permissions.cs`
  and assign it to `PlatformAdmin` / `Operator` / `ProjectAdmin` /
  `Member` in `RoleMatrix`; verify the existing
  `PermissionDemandStartupValidator` accepts the change and the
  build still passes.

## 3. Phase C — Secret store v0

- [ ] 3.1 Create the `Comuki.Modules.Secrets` module
  (Domain / Application / Infrastructure / API), wire its
  installer in `Comuki.Host` and `Comuki.Host.Brain`, and verify
  the solution builds.
- [ ] 3.2 Add a migration for `__comuki_secrets` with tables
  `secrets`, `secret_versions`, `secret_grants`,
  `secret_audit_log` (snake_case columns, indexed by
  `ScopeProjectId`, `OwnerUserId`, `TopicKey`); verify `dotnet ef
  migrations add` produces a clean diff that the build applies.
- [ ] 3.3 Implement `ISSecretProvider` with envelope encryption
  (AES-256-GCM, KEK from `COMUKI_SECRETS_KEK`); unit tests cover
  round-trip, KEK version rotation, and the
  `SecretRefUnsetException` / `SecretRefFormatException` paths.
- [ ] 3.4 Extend `ISSecretResolver` registration in `Host` /
  `Host.Brain` with the new `DbSecretProvider`; verify
  `ProductionSecretAudit` rejects startup when
  `COMUKI_SECRETS_KEK` is unset under `ENV = Production`.
- [ ] 3.5 Add `secret:read` / `secret:use` / `secret:reveal` /
  `secret:write` / `secret:delete` to `Permissions.cs` and
  `RoleMatrix`; verify `PermissionDemandStartupValidator` accepts
  the additions and the existing controllers still pass.
- [ ] 3.6 Implement `/api/v1/secrets` CRUD (list, get, create,
  rotate, rename, reveal, delete, grant, revoke-grant) with
  RBAC filters; verify each endpoint with an integration test in
  `tests/integration/Comuki.Modules.Secrets.Integration/`.
- [ ] 3.7 Register `secrets.list` / `secrets.get` MCP tools; verify
  they return metadata only and respect the caller's subject scope.
- [ ] 3.8 Wire OTel counters `comuki.secrets.resolved`,
  `comuki.secrets.health`, `comuki.secrets.audit`; verify the
  audit log writer emits human actions + `use_error` only, never
  service `use`.
- [ ] 3.9 Add the dashboard `/secrets` page (list, detail with
  reveal gated by `secret:reveal`, audit, grants); replace the
  `<TextField>` `secretEnvRef` in `init-wizard-page`,
  `connection-form`, `connect-source-form` with a `SecretPicker`
  that opens a `SecretField`-style picker over the catalog;
  verify `bun run typecheck && bun run lint && bun run test` exit
  0.

## 4. Phase D — Worker secret injection

- [ ] 4.1 Add `SecretRefs: IReadOnlyList<SecretRefSpec>` to
  `ComputeStartRequest` (additive; existing callers see no change);
  verify the wire builds.
- [ ] 4.2 In the Docker provider, resolve each `SecretRefSpec`
  before `docker run` and emit `-e NAME=value`; failed resolutions
  with `Required = true` SHALL abort the start with
  `ComputeStartError { code = secret.required_unset }`; verify
  with a unit test that mocks `ISSecretResolver`.
- [ ] 4.3 In the Kubernetes provider, resolve each ref and mount a
  per-job `Secret` via `volumeMounts` at `/run/secrets/{EnvName}`;
  the K8s Secret SHALL be deleted by an `OnExit` hook after the
  Job completes.
- [ ] 4.4 Add the logger filter on the worker runtime that drops
  any record matching an injected value and increments
  `comuki.worker.secret_log_suppressed`; verify with a fixture
  that prints `process.env.OPENAI_API_KEY` and sees the record
  dropped.
- [ ] 4.5 Extend the worker's claim body with `secretReferences`
  (env name + one-way hash, never values); verify the journal
  shows only names and hashes.
- [ ] 4.6 Add a per-project setting `Compute:InjectSecrets = true`
  (default `false`); when off, providers ignore `SecretRefs` and
  fall back to existing `Env`; verify the regression path is
  preserved via `tests/unit/Comuki.Engine.Compute.Unit/`.

## 5. Verification across phases

- [ ] 5.1 `dotnet build comuki.slnx -c Debug` exits 0 after each
  phase; warnings-as-errors gates the build.
- [ ] 5.2 `dotnet test tests/unit/...` for the touched projects
  passes after each phase; coverage floor 70% line.
- [ ] 5.3 `cd dashboard && bun run typecheck && bun run lint &&
  bun run test` exits 0 after each phase that touches the FE.
- [ ] 5.4 Integration suite
  `tests/integration/Comuki.Modules.Secrets.Integration/` runs
  against the shared `PostgresFixture` with `Respawner` between
  tests; covers reveal / rotate / grant / RBAC denial paths.
- [ ] 5.5 OpenSpec `openspec validate
  agent-runtime-capabilities` exits 0; sync the delta specs into
  `openspec/specs/` after each phase is complete.

## 6. Rollback hooks (kept off until stable)

- [ ] 6.1 Leave `Compute:InjectSecrets = false` as the default for
  one release after phase 4 lands; remove only after the secrets
  catalog has produced a week's worth of audit data without a
  rotation-related outage.
- [ ] 6.2 Keep the old `secrets` / `discovery` MCP tools
  off-by-default until at least one operator has manually enabled
  them per project; the rollout flag lives in the project's
  settings adapter.
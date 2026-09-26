Workstreams are sized for independent MiniMax/opencode agents on disjoint
file areas — each lists the exact projects/paths it owns so two workstreams
never edit the same file. Deps are hard prerequisites (must merge first);
gates are the commands each workstream's own agent runs before handing back.
The orchestrator still runs `dotnet build comuki.slnx -c Debug` and the full
test suites across workstreams before archiving this change.

## 1. Repositories module skeleton

- [x] 1.1 Create `Comuki.Modules.Repositories.{Domain,Application,Infrastructure}`
  projects under `platform/src/modules/Repositories/`, add to `comuki.slnx`
  by hand, wire ProjectReferences (Domain ← Application ← Infrastructure, no
  reference to Compute/Projects/Orchestration implementations — port
  interfaces only); verify `dotnet sln comuki.slnx list` shows all three and
  `Comuki.Architecture.Tests` (new suite entry) asserts no forbidden
  reference.
- [x] 1.2 Add `RepositoriesDbContext` (schema `repositories`, module-private
  migrations history `__comuki_repositories`), installer
  `AddRepositoriesModule`, and a Migrator loop entry; verify a Debug build of
  the three projects succeeds and the migrator applies an empty schema.

Deps: none. Files: `platform/src/modules/Repositories/**`, `comuki.slnx`,
`platform/src/host/Comuki.Migrator/**` (append-only registration).
Gates: `dotnet build platform/src/modules/Repositories/**/*.csproj -c Debug`.

## 2. Repository, RepositoryPolicy, RepositoryCredentialRef domain + persistence

- [x] 2.1 Implement `Repository` (url, host, default branch), `RepositoryPolicy`
  (protected branches, required checks, approvers), `RepositoryCredentialRef`
  (integration ref, default access `write`|`read`) aggregates per
  `design.md`'s entity block; verify unit tests reject a duplicate
  `(host, url)` registration and cover `effective = min(attachment.access,
  credential.DefaultAccess)` resolution as a pure function.
- [x] 2.2 Add EF configurations + `dotnet ef` generated migration (never
  hand-edit) for `repositories`, `repository_policies`,
  `repository_credential_refs`; verify migrator creates the schema and the
  model snapshot matches.
- [x] 2.3 Implement `IRepositoryStore` port + EF store; verify integration
  test (Testcontainers) round-trips create/get/list.

Deps: 1. Files: `platform/src/modules/Repositories/Comuki.Modules.Repositories.Domain/**`,
`.Infrastructure/Migrations/**`, `.Infrastructure/Persistence/**`.
Gates: `dotnet run --project tests/unit/Comuki.Modules.Repositories.Unit`
(new project, created in this workstream); T1 integration via Testcontainers
Postgres.

## 3. ProjectRepositoryAttachment (Projects module)

- [ ] 3.1 Add `ProjectRepositoryAttachment` (role, access, optional credential
  override) to `Comuki.Modules.Projects.Domain`, referencing `ProjectId`
  internally and a `RepositoryId` value (no reference to
  `Comuki.Modules.Repositories` implementation types — mirrors how
  Orchestration references `ProjectId` by value today); verify unit tests
  cover attach/detach and that two attachments to the same Repository from
  different Projects are independent (spec scenario 1 and 2 of the ADDED
  "Project repository attachments" requirement).
- [ ] 3.2 Add EF configuration + `dotnet ef` migration for
  `project_repository_attachments` in `ProjectsDbContext`; verify migrator
  applies cleanly alongside existing Projects tables.
- [ ] 3.3 Add `IProjectRepositoryAttachmentStore` port + EF store +
  `Application` command/query handlers (attach, detach, list-by-project,
  list-by-repository); verify integration test round-trip.

Deps: 2 (needs `RepositoryId` to reference). Files:
`platform/src/modules/Projects/Comuki.Modules.Projects.Domain/Attachments/**`
(new subfolder — does not touch existing `Projects/`, `Settings/`,
`DomainTypes/` files), `.Application/Attachments/**`,
`.Infrastructure/Migrations/**` (new migration file only),
`.Infrastructure/Persistence/Configurations/ProjectRepositoryAttachmentConfiguration.cs`,
`.Infrastructure/Persistence/Stores/DbProjectRepositoryAttachmentStore.cs`.
Gates: `dotnet run --project tests/unit/Comuki.Modules.Projects.Unit`; T1
integration under `tests/integration/Comuki.Modules.Projects.Integration.Migrations`.

## 4. RepositoryLink + auto-discovery

- [ ] 4.1 Implement `RepositoryLink` (type, artifact, versionConstraint,
  status `suggested`|`confirmed`, discoveredBy, driftDetectedAt) aggregate
  and `IRepositoryLinkStore`; verify unit tests cover the confirm transition
  and that drift detection never auto-reverts `confirmed` to `suggested`
  (spec scenario).
- [ ] 4.2 Implement discovery adapters (`.gitmodules`, `PackageReference`/
  `package.json`, OpenAPI client config, helm/kustomize values, fork
  remotes) behind one `ILinkDiscoveryAdapter` per manifest kind, run on
  attach and on a periodic schedule; verify unit tests per adapter against
  fixture manifest files and an integration test that a discovery pass
  writes `suggested` links without touching existing `confirmed` ones.

Deps: 2. Files: `platform/src/modules/Repositories/Comuki.Modules.Repositories.Domain/Links/**`,
`.Application/Discovery/**`, `.Infrastructure/Discovery/**`.
Gates: unit suite + T1 integration (fixture manifests on disk, no external
network).

## 5. ArtifactSource adapters

- [ ] 5.1 Implement `IArtifactSource` port (`ResolveReadiness(VersionConstraint)
  -> ready|pending`) and adapters for NuGet feed, npm feed, container
  registry, git tag, merge-to-branch, CI status, each with a poll
  implementation and a webhook-ingest entry point where the kind supports
  one; verify unit tests per adapter with a faked feed/registry response.
- [ ] 5.2 Wire adapter selection by `RepositoryLink.Type` and expose a
  readiness query used by workstream 6's DAG wait; verify integration test:
  a `pending` constraint blocks, a satisfied one resolves.

Deps: 4. Files: `platform/src/modules/Repositories/Comuki.Modules.Repositories.Application/ArtifactSources/**`,
`.Infrastructure/ArtifactSources/**`.
Gates: unit suite; T1 integration with a stub HTTP feed server.

## 6. Cross-repo DAG, external block, ExternalChangeRequest

- [ ] 6.1 Implement task decomposition for a cross-repo change into
  per-repository DAG tasks (target repo rw + capped confirmed-neighbour
  context) and the artifact-wait gate consuming workstream 5's readiness
  query; verify unit tests on DAG ordering and the neighbour cap truncation
  (never blocking on over-cap).
- [ ] 6.2 Implement `ExternalChangeRequest` (channel selection: tracker-issue
  / draft-for-human / fork-pr, Brain-authored capability spec, `blocked-
  external` task status, watcher, verify-before-unblock); verify unit tests
  cover the "watcher fires but Brain rejects" scenario staying blocked.
- [ ] 6.3 Repo-scope the existing merge queue: add `RepositoryId` to
  `MergeQueueEntry`/`MergeBatch` queries in
  `Comuki.Engine.Orchestration` (additive column/index, `ProjectId` stays as
  today — no removal), so two Projects with write attachments to one
  Repository share its single queue; verify existing
  `MergeQueueEntryShould`/`MergeMergeQueueHandlerShould` tests stay green
  and a new test proves two Projects' entries interleave in one Repository's
  queue.
- [ ] 6.4 Implement the backward-compatibility contract check before an
  upstream merge and the automatic-propose/approve-to-merge revert-PR path;
  verify unit tests for both the compatible-merge and the confirmed-breakage
  revert-proposal paths.

Deps: 5. Files: `platform/src/modules/Repositories/Comuki.Modules.Repositories.Application/Dag/**`,
`.Application/ExternalRequests/**`,
`platform/src/engine/Comuki.Engine.Orchestration/Domain/MergeQueue/**`
(additive changes only — coordinate with any in-flight merge-queue work
before editing shared files), new migration for the `RepositoryId` column.
Gates: unit suite; T1 integration (Testcontainers); rerun
`tests/unit/Comuki.Engine.Orchestration.Unit.StatusMachine` to confirm no
regression.

## 7. Worker-runtime multi-repo workspace composition

- [ ] 7.1 Extend the Host claim resolution (`Comuki.Host.Workers.Workspace`)
  to resolve a target repository (via `ProjectRepositoryAttachment`,
  falling back to scalar `SourceGitUrl`/`SourceGitRef` per the migration
  requirement) plus confirmed-neighbour repositories capped at a configured
  limit, and extend the claim payload with a neighbour list (url, ref,
  slug — no write credential); verify unit tests cover the cap-truncation
  and no-neighbour-unaffected scenarios from the spec delta.
- [ ] 7.2 Extend `Comuki.Host.Translator.Execution.Workspace`
  (`ISourceWorkspacePreparer`/new neighbour preparer) to materialize
  neighbours under `context/<repository-slug>/` read-only, leaving the
  existing target-repo-at-root behavior untouched; verify an integration
  test proves neighbour directories are present and the target repo's
  existing single-repo test fixtures still pass unmodified.

Deps: 3, 4 (needs attachments + confirmed links to resolve a graph); should
land after `feature/source-workspace-clone` (#125) merges, since it extends
`ClaimWorkspace`/`ISourceWorkspacePreparer` — coordinate branch order with
whoever lands #125 first. Files:
`platform/src/host/Comuki.Host/Workers/Workspace/**`,
`platform/src/host/Comuki.Host.Translator/Execution/Workspace/**`.
Gates: unit suite; T1 integration; extend
`tests/integration/Comuki.Host.Translator.Integration.PiCli/TranslatorE2EShould.cs`.

## 8. Scalar source migration

- [ ] 8.1 Implement the one-time migration: for every Project with non-null
  `SourceGitUrl`, register/match a `Repository` and create the
  `primary`/`write` attachment carrying `SourceGitRef`; verify a migration
  integration test against Testcontainers fixtures covering a Project with
  no source, with source and no ref, and with source and ref.
- [ ] 8.2 Switch worker workspace resolution (workstream 7) to prefer the
  primary attachment once present, scalar fields as fallback only; verify an
  integration test proves a Project with both an attachment and stale scalar
  fields resolves from the attachment.

Deps: 3, 7. Gated on `feature/source-workspace-clone` (#125) having merged —
if it has not merged by the time this workstream starts, stub the migration
against a local branch fixture and flag it non-blocking in the PR
description. Files:
`platform/src/modules/Projects/Comuki.Modules.Projects.Application/Migration/**`
(new subfolder).
Gates: T1 integration.

## 9. REST + host composition

- [ ] 9.1 Add controllers under `/api/v1/repositories` (register, list, get)
  and `/api/v1/projects/{projectId}/repository-attachments` (attach, detach,
  list) with FV validators and `RequiresPermission` (new `repository:read` /
  `repository:write` keys added to the identity catalog); verify 201/404/409
  paths and a boot-time catalog test.
- [ ] 9.2 Wire `Comuki.Modules.Repositories` + attachment endpoints in
  `HostComposer`; verify Debug build emits `artifacts/openapi.json`
  containing the new paths.

Deps: 2, 3. Files: `platform/src/host/Comuki.Host/Repositories/**` (new),
`platform/src/host/Comuki.Host/Projects/**` (attachment endpoints only, new
files), `Comuki.Host/HostComposer.cs` (append-only registration line).
Gates: full `dotnet build comuki.slnx -c Debug`.

## 10. Tests + docs

- [ ] 10.1 T1 integration: attachment CRUD, link discovery, artifact-source
  adapter polling — Testcontainers Postgres, already covered per-workstream
  above; this task is the consolidated pass verifying no cross-workstream
  regression.
- [ ] 10.2 T2 scenario: two fake git repositories + `tests/tools/Comuki.AgentTest.Runner`
  fake model — cross-repo DAG with artifact wait (upstream merges, artifact
  publishes, downstream bumps and merges) and external block (a task targets
  a repo with `access: external`, verify `blocked-external` + no write
  attempted + `ExternalChangeRequest` created).
- [ ] 10.3 T3 `deploy/compose.e2e.yml`: two real git fixture repositories (one
  upstream package, one downstream consumer with a `package-pin` link),
  prove the full register → attach → discover → confirm → DAG → merge cycle
  end to end, plus a submodule-pointer-bump fixture proving no worker is
  spawned for that step.
- [ ] 10.4 Architecture test: `Comuki.Modules.Repositories` does not
  reference `Comuki.Engine.*` or `Comuki.Host.*` implementations; verify
  `Comuki.Architecture.Tests` stays green.
- [ ] 10.5 Document the module under `.agents/docs/architecture/` (repo
  registry, attachment model, DAG/artifact-wait, external-block) and mention
  the slice in `.agents/STATE.md`/`ROADMAP.md`.

Deps: 6, 7, 8, 9. Gates: full `dotnet build comuki.slnx -c Debug`
(warnings-as-errors + format) + full unit suite + T1/T2 (T3 requires Docker/
Podman — run only where the docker gate is available, per this repo's
existing docker-availability skip convention).

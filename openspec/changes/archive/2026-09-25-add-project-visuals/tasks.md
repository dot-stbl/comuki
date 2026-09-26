# Project Visual Identity — Tasks

Order: BE domain → migration → wire → BE tests → FE contract → FE UI →
FE tests → gates. Verification is stated per task; the gate commands are
the repo's own (`dotnet build comuki.slnx -c Debug`,
`dotnet run --project …`, `cd dashboard && bun run typecheck && bun run
lint && bun run test`).

## 1. Domain layer

- [x] 1.1 Extend `Project` (`platform/src/modules/Projects/Comuki.Modules.Projects.Domain/Projects/Project.cs`) with `Icon` (string?, max 200), `Color` (string?, `#rrggbb` lower-case), `Tags` (string[], default `[]`); add public constants `MaxIconLength = 200`, `MaxTags = 20`, `TagPattern`, `ColorPattern` and a tag/colour normaliser following the `DomainTypeAdmission.NormalizeKey` precedent (trim + `ToLowerInvariant`, drop empties, de-duplicate, colour to lower-case). Extend `Create` and `Update` to accept and normalise the three fields, keeping PATCH null-means-untouched (absent tags list untouched; empty list clears). Verify: new cases in `ProjectDomainShould` pass (`dotnet run --project tests/unit/Comuki.Modules.Projects.Unit`)
- [x] 1.2 Confirm no sibling module reads a project field this widening breaks (grep `platform/src` for `ProjectView(` constructions outside the Projects module) — verify by `dotnet build comuki.slnx -c Debug` after section 3

## 2. Infrastructure + migration

- [x] 2.1 Extend `ProjectConfiguration` (`…Infrastructure/Persistence/Configurations/ProjectConfiguration.cs`): `icon` (`text`, max length 200, NULL), `color` (`text`, fixed 7, NULL), `tags` (`text[]` NOT NULL DEFAULT '{}') — snake_case names, mirroring `DomainTypeAdmissionConfiguration`'s array mapping
- [x] 2.2 Generate the migration: `dotnet ef migrations add AddProjectVisualIdentity --context ProjectsDbContext --project platform/src/modules/Projects/Comuki.Modules.Projects.Infrastructure --startup-project platform/src/host/Comuki.Migrator` (adjust startup project to the repo's actual migrator); verify the generated `Up` adds exactly the three columns and the snapshot (`ProjectsDbContextModelSnapshot.cs`) reflects them
- [x] 2.3 Extend `ProjectsMigrationsShould` (`tests/integration/Comuki.Modules.Projects.Integration.Migrations`) to assert the new migration applies over a fresh database and over the previous head, and that a pre-change row reads `icon IS NULL AND color IS NULL AND tags = '{}'` (requires the local test runtime; if unavailable in the session, mark blocked and say so — do not delete)

## 3. Application layer + host wire

- [x] 3.1 Widen `CreateProjectCommand` / `UpdateProjectCommand` (`…Application/Projects/Create|Update/`) with `Icon`, `Color`, `Tags` (`IReadOnlyList<string>?` on update — null untouched, empty clears, per design D5); extend both handlers to pass them into `Project.Create` / `Project.Update`
- [x] 3.2 Extend `CreateProjectValidator` / `UpdateProjectValidator`: icon max 200, colour `ColorPattern`, per-tag `TagPattern` after trim, `MaxTags` on the distinct normalised list — errors land on `Icon` / `Color` / `Tags` properties. Verify: new cases in `CreateProjectValidatorShould` / `UpdateProjectValidatorShould` (malformed colour, bad tag, >20 tags, oversized icon, valid pass)
- [x] 3.3 Extend `ProjectView` with `Icon`, `Color`, `Tags` and the `ProjectMapper.ToView(Project)` projection to populate them
- [x] 3.4 Widen the host wire: `CreateProjectRequest` / `UpdateProjectRequest` (`platform/src/host/Comuki.Host/Projects/Models/`) gain optional `icon` / `color` / `tags`; extend `ProjectsEndpointMapper.ToCommand` overloads. Verify: `dotnet build comuki.slnx -c Debug` → 0 warnings / 0 errors (permissions unchanged: `project:admin` writes, `project:read` reads)
- [x] 3.5 Extend `ProjectHandlersShould`: create-with-identity returns the normalised view; patch-tags-only changes tags and nothing else; patch with absent tags keeps the list (handler-level, no HTTP)

## 4. FE contract

- [x] 4.1 Regenerate the kubb client against the updated OpenAPI: `cd dashboard && bun run generate-api` (builds the slnx, runs kubb, prettier-writes); verify `src/shared/api/_generated/types/CreateProjectRequest.ts`, `UpdateProjectRequest.ts` and the project view type carry `icon` / `color` / `tags` and the commit is a pure regen
- [x] 4.2 Extend `model/types.ts`: `ProjectRow` gains `icon: string | null`, `color: string | null`, `tags: string[]`; `CreateProjectInput` and `ProjectUpdate` (in `api/mutations.ts`) gain the three fields
- [x] 4.3 Extend `api/mappers.ts`: widen the local `ProjectView` wire interface; `mapProjectViewToDetail` and `toProjectRow` carry the three fields (tolerant defaults `null / null / []` per the mapper's own convention); `mapCreateProjectInputToCreateRequest` sends them; `updateProject` body passes `icon / color / tags` through preserving absent-vs-empty tags (design D5). Verify: `mappers.test.ts` extended — legacy wire row without the fields maps to `null/null/[]`, create/update bodies carry the fields
- [x] 4.4 Add `model/identity.ts`: pure `resolveProjectMark(project)` → `{ kind: "stored", value } | { kind: "brand", brand: "github" | "gitlab" | "git" } | { kind: "neutral" }` mapping the host out of `ProfilesGitUrl` (`git@host:…` and `https://host/…` shapes, per design D3), plus an `isImageIcon(value)` scheme check. Verify: new `identity.test.ts` covers both URL shapes, unknown host, no-URL, stored-icon-wins

## 5. Mock parity

- [x] 5.1 Extend `SeedProject` and `PLATFORM_PROJECTS_SEED` (`dashboard/src/shared/api/mock/projects.seed.ts`): `p_comuki` gets an emoji icon + colour + tags, `p_plexor` (GitLab URL) stays bare to exercise derivation, `p_atlas` gets colour + tags but no icon, `p_vega` stays bare; extend `createSeedProject` (`projects.store.ts`) to accept and persist the three fields. Verify: `bun run test` — existing seed-consuming tests updated where snapshots/assertions changed

## 6. FE UI

- [x] 6.1 Registry identity cell (`ui/projects-columns.tsx`): the slug column's cell (or a narrow leading column, matching the table's density) renders the resolved mark + accent dot (`--project-accent` custom property, design D7) and a tags cell renders the chips (tint via `color-mix`, `--project-accent` fallback to muted ink); degrade honestly — no icon, no dot colour, no tags → current rendering. Verify: `projects-table.test.tsx` extended (mark shown for stored icon, derived for GitHub/GitLab URL, absent for bare row)
- [x] 6.2 Tag filter in the toolbar (`pages/projects-page.tsx`): a multi-select chips control fed by the distinct tags of the loaded rows, AND-semantics applied in the `rows` memo alongside `applyDataFilters`; shown-count reflects the narrowed set; clearing restores. No new list-endpoint params (design D6). Verify: page-level test — select `billing` narrows, select two tags intersects, clear restores
- [x] 6.3 Create form (`ui/create-project-form.tsx`): three optional inputs — icon text field (emoji or URL), colour picker (native `<input type="color">` styled to the kit, or the kit's equivalent), tags input with chip entry (Enter/comma adds, Backspace removes, duplicate refused); all optional, never disable submit on them; dirty-tracking extended. Verify: `create-project-form.test.tsx` extended (chips add/remove/dedupe, submit payload carries the fields, empty stays empty)
- [x] 6.4 Detail identity block (`pages/project-detail-page.tsx`): "the project itself" grows icon/colour/tags facts and an inline edit affordance writing through the widened `useUpdateProjectMutation` (design D8) — permission-gated the way the page's other manage affordances are. Verify: `project-detail-page.test.tsx` extended (facts render, edit writes the patch, absent tags key not coerced to `[]`)
- [x] 6.5 Update stories (`projects-table.stories.tsx`, `project-detail-page.stories.tsx`) with identity-bearing fixtures; `bun run test:storybook` (or the repo's visual gate) stays green

## 7. Backend tests — consolidation

- [x] 7.1 Run the full Projects unit project: `dotnet run --project tests/unit/Comuki.Modules.Projects.Unit -c Debug` → green (existing baseline + the new validator/domain/mapper/handler cases from 1.1, 3.2, 3.5)
- [x] 7.2 Run the architecture tests: `dotnet run --project tests/Comuki.Architecture.Tests -c Debug --no-build` → green (no new cross-layer references introduced)

## 8. Gates

- [x] 8.1 `dotnet format comuki.slnx --severity hidden` then `dotnet build comuki.slnx -c Debug` → 0 warnings / 0 errors
- [x] 8.2 `cd dashboard && bun run typecheck && bun run lint && bun run test` → all exit 0
- [x] 8.3 `cd dashboard && bun run build` → exit 0 (kubb client and prettier-clean regen included)
- [x] 8.4 Self-audit per `worker-audit.md` §2b on the changed files (naming, private methods, `_ =` discards, `List<T>` surfaces) — findings fixed or justified in the commit

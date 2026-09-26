## Context

See proposal.md for why. Constraints that shape the how:

- `Project` (`platform/src/modules/Projects/...Domain/Projects/Project.cs`)
  carries Id/Name/Slug/Description/ProfilesGitUrl/ProfilesGitRef/archive
  stamps; `ProjectView` mirrors it 1:1. Commands
  (`CreateProjectCommand` / `UpdateProjectCommand`), FluentValidation
  validators, the host `Projects/Models` + `ProjectsEndpointMapper` and
  the FE `ProjectRow`/`CreateProjectInput`/mappers all repeat the same
  field set — the change is a straight widening of that spine.
- PATCH is null-means-untouched end to end (`Project.Update`); the slug
  normalisation precedent is trim + `ToLowerInvariant` then pattern
  check, and `DomainTypeAdmission.NormalizeKey` repeats it — tags follow
  the same school.
- The module already stores `string[]` as native Postgres `text[]`
  (`DomainTypeAdmission.AllowedSources` / `DeniedReasons`, Npgsql maps
  `string[]` straight onto `text[]`). The only `jsonb`-ish precedent is
  `ProjectSettings.CustomDomainTypesJson`, and that one is a `text`
  column holding a lazily-structured map, not a list.
- The dashboard registry loads the full project list once
  (`useProjectsQuery`, shared `["projects"]` cache) and narrows
  client-side through the DataTable toolbar (`applyDataFilters` +
  per-column `meta.filter`). No pagination envelope exists on
  `GET /api/v1/projects`.
- There is **no project edit UI today**: `useUpdateProjectMutation`
  exists (name/description) but nothing calls it. The create form is
  three fields at `/projects/new`; the detail page shows a `FactList`
  ("the project itself") of the same facts.
- DESIGN.md: colourless chrome, seven status colours with the
  two-channel rule, tokens in `tokens.css`. Saturated colour is allowed
  **inside data**; chrome stays grey. No i18next — dashboard copy is
  literal English; FE tests match on those literals.
- `openspec/changes/harden-pi-worker-sandbox` also carries a MODIFIED
  "Slug is immutable" delta (it widens the same PATCH list with source
  git fields). Both deltas modify the same requirement against the same
  base; whichever archives second must re-merge the field list. Noted
  here so the archiver doesn't lose a field.

## Goals / Non-Goals

**Goals:**

- One widening of the project spine (entity → command → validator →
  view → wire → kubb client → FE types/mappers → UI) with no new
  endpoints and no breaking wire change.
- Server-side validation that makes an invalid colour/tag/icon
  impossible to store, so every consumer can render identity fields
  unguarded.
- Tag filtering that works with the registry the product already has
  (full list in memory) and a recorded threshold for flipping it
  server-side.

**Non-Goals:** (design-level)

- Tag dictionary/autocomplete, tag colours, per-workspace tag
  governance — free-form per project, per this change and the proposal's
  non-goals.
- Any server-side provider detection (see D3).
- Snap-to-palette accent colours (see D7).

## Decisions

### D1. Tags stored as `text[]` (`string[]`), not jsonb

Follows `DomainTypeAdmission`: Npgsql maps `string[]` onto `text[]`
natively, the column stays a flat list of scalars, and a future
server-side filter (`WHERE tags && ARRAY[...]`) stays expressible and
indexable (GIN) without a migration. `jsonb` was considered via the
`CustomDomainTypesJson` precedent and rejected: that column is a
lazly-structured map a consumer parses; tags are a closed-shape list the
SQL layer should be able to see into. Column: `tags text[]` NOT NULL
DEFAULT '{}'; `icon text NULL` (max 200), `color text NULL` (fixed 7
chars by validation).

### D2. Icon is an opaque string; the server validates length only

Discriminating emoji vs URL server-side is a rabbit hole (grapheme
clusters, ZWJ sequences) and a URL-shaped regex would reject every
emoji. The contract is "display string ≤ 200"; how to render it (text
vs `<img>`) is decided client-side by the single rule "starts with a
URL scheme → image, else text". **Alternative:** a discriminated
`iconType` field — rejected; two wire fields for one display concern,
and the FE heuristic is total (every string renders as one or the
other).

### D3. Provider-derived mark is FE-only; no `IconSource` enum

The backend stores only explicit overrides. When `icon` is null the FE
derives a brand mark from the `ProfilesGitUrl` host (github.com → GitHub
mark, gitlab* → GitLab logo, else generic git glyph; no URL → neutral
project glyph). Deriving server-side would freeze the mark at write time
(go stale when the repo moves), couple the Projects module to provider
knowledge it does not need, and require an extra `IconSource` column
whose only consumer is a renderer. The derivation lives in one pure FE
helper (`resolveProjectMark(project)`), unit-tested — mapping the host
out of both `git@host:…` and `https://host/…` shapes.

### D4. Tag/colour normalisation follows the slug precedent

Trim + `ToLowerInvariant` first, then pattern validation — the same
order `Project.Create` applies to the slug and `NormalizeKey` applies to
admission keys. Tags: trim each entry, drop empties, de-duplicate
(case-insensitively, which after lower-casing is plain distinct), then
enforce ≤ 20 and the per-tag pattern. Colour: accept
`^#[0-9A-Fa-f]{6}$` either case, store lower-case. Normalisation lives
in the domain (`Project.Create` / `Project.Update`), so the validators
check the *pre-normalisation* shape only for what normalisation cannot
fix (pattern after trimming, count, icon length) — no double
validation in the handler.

### D5. PATCH wire semantics: absent tags ≠ empty tags

`UpdateProjectCommand.Tags` is `IReadOnlyList<string>?` — `null` (absent
JSON key) leaves the stored list untouched, `[]` clears it. This is the
one list-vs-scalar asymmetry in an otherwise uniform PATCH contract; it
is called out in the spec scenario so the FE mutation layer preserves
the distinction (never `tags: patch.tags ?? []`). Icon and colour are
plain nullable strings with the existing null-means-untouched rule.

### D6. Tag filtering stays client-side; the flip threshold

The registry already loads and holds the full list; AND-semantics over
an in-memory array is O(rows × selected tags). Flip server-side
(`GET /api/v1/projects?tags=a,b` + a distinct-tags summary endpoint)
when any of: project count passes ~500, the list payload passes ~1 MB,
or the distinct-tag vocabulary outgrows a chips control (~50). Below
that, server params are wire surface with no user-visible benefit.
Recorded here as the agreed trigger, not implemented by this change.

### D7. Accent colour is data, applied through one custom property

Per DESIGN.md, saturated colour lives inside data surfaces only. The
stored hex never touches chrome: it paints the small dot beside the
identity mark and tints tag chips. Mechanism: the row's component sets
one inline custom property (`style={{ "--project-accent": color }}`)
and the CSS module consumes it (`background: var(--project-accent)`,
chip tint via `color-mix(in oklab, var(--project-accent) 15%,
transparent)`). This is the sanctioned shape for data-driven colour —
inline *custom property*, not inline `background-color`, so the value
stays addressable by the stylesheet and the "no inline style colour"
lint posture (stories excepted) is preserved. Fallback when absent: the
muted-ink token of the surface. The colour is never the sole carrier of
meaning — slug and name identify the row; a greyscale render loses
nothing operative. **Alternative:** snap the stored colour to the
nearest of the seven status hues — rejected: identity is per-project
data, not one of seven statuses, and snapping would make two projects'
accents collide by construction.

### D8. Editing lives on the detail page; the create form widens in place

No edit surface exists today, so this change defines one minimally: the
create form gains three optional inputs (icon text field, colour
picker, tags input with chip entry); the detail page's "the project
itself" section grows the identity facts and an inline edit affordance
(eye-triggered panel writing through `useUpdateProjectMutation`, widened
to carry icon/colour/tags). No separate `/projects/:id/edit` route — an
edit is three optional fields, not a page. The mutation's current
hardcoded `profilesGitUrl: null` pass-through keeps working under
null-means-untouched. **Assumption to surface:** if the owner wants a
full project-edit page instead, that is a follow-up change, not this
one.

### D9. Mock parity

`SeedProject` grows `icon`, `color`, `tags` (one GitHub-hosted with
icon override, one GitLab-hosted relying on derivation, one with
colour + tags, one bare); `createSeedProject` accepts the three fields;
`toProjectRow` / `mapProjectViewToDetail` carry them so mock and real
modes stay shape-identical — the existing no-domain-drift rule for the
mock path.

## Risks / Trade-offs

- **[Risk] Two deltas modify "Slug is immutable"** (this and
  `harden-pi-worker-sandbox`) → whoever archives second re-merges the
  PATCH field list; both deltas textually widen the same sentence, so
  the merge is mechanical but must not be skipped.
- **[Risk] Free-form tags drift into a vocabulary** (case variants,
  plurals) → normalisation lower-cases and trims; a global dictionary is
  an explicit non-goal and a future change if wanted.
- **[Risk] Arbitrary hex on a data surface can fight both themes**
  (light/dark) → the colour paints a dot and chip tints via
  `color-mix`, never text on the lane; contrast floors do not apply to
  decoration, and identity never rides on the colour alone.
- **[Risk] Emoji-as-icon renders differently across platforms** →
  accepted: the operator picks the emoji and sees it in the picker
  before saving; no server-side grapheme validation (D2).
- **[Trade-off] `text[]` makes ad-hoc SQL slightly noisier than jsonb**
  → worth it for native containment operators later (D1).

## Migration Plan

One additive EF migration (`AddProjectVisualIdentity` or similar name
from `dotnet ef migrations add`) on `ProjectsDbContext`: three nullable
columns on `projects` (`icon text NULL`, `color text NULL`,
`tags text[] NOT NULL DEFAULT '{}'`). Existing rows read as
`null / null / []` — the spec's legacy-view scenario pins that. Rollback
is the migration's `Down` (drop three columns); no data conversion in
either direction. The migrations integration test
(`ProjectsMigrationsShould`) extends to assert the new columns apply
cleanly over a fresh database and over the previous head.

## Open Questions

None blocking. The server-side-flip threshold (D6) and the full
edit-page follow-up (D8) are recorded trigger conditions, not unknowns.

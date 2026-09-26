## Why

Projects are the scope unit every list in the product names, but on screen
they are indistinguishable: no mark, no colour, no grouping vocabulary.
With a handful of projects the registry reads fine; past that, the
operator scans a wall of identical rows. This change gives each project
an operator-chosen visual identity — an icon override, an accent colour,
and a small set of tags — and a tag filter in the registry so the list
can be narrowed by what the projects *are*, not only by what they are
named.

## What Changes

- `Project` gains three optional identity fields, stored and served on
  every view:
  - `Icon` — nullable string override (a single emoji or an image URL;
    opaque display string, ≤ 200 chars).
  - `Color` — nullable `#RRGGBB` hex accent.
  - `Tags` — list of slug-like tags (`^[a-z0-9][a-z0-9-]{0,38}$`),
    max 20 per project, deduplicated, order-insensitive.
- Create and PATCH accept the three fields (PATCH keeps null-leaves-
  untouched semantics; an empty tags list clears tags); views return
  them. Server-side FluentValidation: colour regex, tag pattern/count,
  icon length → 400 with per-field errors. Permissions unchanged
  (`project:admin` to write, `project:read` to read).
- Provider-derived icon **default** is a frontend concern: when no
  stored `Icon` exists, the dashboard derives a brand mark from
  `ProfilesGitUrl` host (github → GitHub mark, gitlab* → GitLab logo,
  else generic git). The backend stores only explicit overrides — no
  provider detection server-side, no `IconSource` enum.
- Tag **filtering** is client-side in the registry (the list endpoint
  gains no new query params); the threshold at which that flips
  server-side is recorded in design.md.
- Dashboard: create/edit form gains icon, colour and tags inputs; the
  registry shows the identity mark, accent and tags; the toolbar gains
  a tag filter; the detail page gains an identity block; mock seed and
  mappers updated; kubb client regenerated.
- Persistence: three new nullable columns on `projects` (`icon text`,
  `color text`, `tags text[]`) via `dotnet ef migrations add` only.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `projects`: new requirement "Project visual identity" (fields,
  normalisation, validation, wire shape); the "Slug is immutable"
  requirement's PATCH field list widens to include icon, colour and
  tags.

## Impact

- Backend: `Comuki.Modules.Projects` (Domain/Application/Infrastructure
  — entity, commands, validators, views, EF configuration, one
  migration), `Comuki.Host/Projects` (request models, endpoint mapper).
- Frontend: `dashboard/src/domains/projects/` (types, mappers, form,
  columns, pages), `dashboard/src/shared/api/mock/projects.seed.ts`,
  regenerated `dashboard/src/shared/api/_generated/`.
- Tests: `tests/unit/Comuki.Modules.Projects.Unit` (validators, domain,
  handlers, mapper) and the Projects migrations integration test.
- No new endpoints, no new permissions, no breaking wire change (all
  additions optional and nullable).

## Non-goals

- Server-side provider detection or an `IconSource` enum — the stored
  icon is an opaque override; derivation from the git host is a
  display default only.
- Server-side tag filtering / a tag dictionary endpoint — stays
  client-side until the registry outgrows a full-list load (threshold
  in design.md).
- Tag autocomplete from a global vocabulary, tag colours, or
  per-workspace tag management — tags are free-form per project.
- Icons as uploaded binaries — the field accepts an emoji or a URL
  only; no object storage involvement.
- Theming integration — the accent colour is project data rendered
  inside data surfaces; it does not touch the theme registry or
  `tokens.css` semantics.

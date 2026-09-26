# Project Visual Identity — Delta

## ADDED Requirements

### Requirement: Project visual identity

A project SHALL carry three optional identity fields, stored with the
project and served on every project view:

- `icon` — nullable opaque display string, max length 200 (a single emoji
  or an image URL; the server does not interpret which).
- `color` — nullable accent colour matching `^#[0-9A-Fa-f]{6}$`; accepted
  in either case, stored and served lower-case (`#rrggbb`).
- `tags` — list of slug-like tags, each matching
  `^[a-z0-9][a-z0-9-]{0,38}$` after trimming and lower-casing, at most 20
  distinct tags per project.

On create and update the server SHALL normalise tags (trim each entry,
lower-case invariantly, drop empty entries, remove duplicates) and colour
casing before persisting. An absent field on create means `null` icon,
`null` colour and an empty tag list.

#### Scenario: Create with identity fields

- **WHEN** a project is created with `icon = "🛰️"`,
  `color = "#3C5A86"` and `tags = ["web", "Billing"]`
- **THEN** subsequent reads return the icon verbatim, the colour as
  `#3c5a86`, and the tags as `["web", "billing"]`

#### Scenario: Duplicate and padded tags collapse

- **WHEN** a create or PATCH sends
  `tags = [" web ", "web", "", "WEB"]`
- **THEN** the stored tag list is `["web"]` — one entry, trimmed and
  lower-cased

#### Scenario: Oversized tag list refused

- **WHEN** a PATCH sends 21 distinct valid tags
- **THEN** the answer is 400 with a per-field error on `tags` and nothing
  is persisted

#### Scenario: Malformed colour refused

- **WHEN** a create sends `color = "3c5a86"` (missing `#`) or
  `"#3C5A8"` (five digits)
- **THEN** the answer is 400 with a per-field error on `color`

#### Scenario: Malformed tag refused

- **WHEN** a create sends a tag `Data_Pipeline` (underscore) or
  `-infra` (leading dash)
- **THEN** the answer is 400 with a per-field error on `tags`

#### Scenario: Oversized icon refused

- **WHEN** a create sends an `icon` longer than 200 characters
- **THEN** the answer is 400 with a per-field error on `icon`

### Requirement: Identity PATCH semantics

Update SHALL treat the three identity fields with the same PATCH
semantics as the other project fields — `null` leaves the stored value
untouched — with one wire-level distinction for the list field: an absent
(`null`) `tags` field leaves the stored tags unchanged, while an empty
array `[]` clears them. `null` icon or colour on a PATCH means
"untouched"; an explicit empty string is invalid (falsy is not null) and
answers 400 the same way it does for name.

#### Scenario: Patch colour only

- **WHEN** a caller patches only `color`
- **THEN** the colour changes and icon and tags stay as they were

#### Scenario: Empty tags list clears tags

- **WHEN** a caller patches `tags = []`
- **THEN** subsequent reads return an empty tag list

#### Scenario: Absent tags field keeps tags

- **WHEN** a caller patches `name` only, with no `tags` key in the body
- **THEN** the stored tags are unchanged

### Requirement: Identity marks in the registry

The dashboard SHALL render each project with an identity mark and its
tags: the registry list, the create/edit forms and the project detail
page all participate. The mark resolves in this order:

1. the stored `icon`, when present — an emoji renders as text, a URL
   renders as an image;
2. otherwise a mark derived client-side from the `ProfilesGitUrl` host —
   `github.com` yields the GitHub mark, a `gitlab*` host yields the
   GitLab logo, any other host a generic git glyph;
3. no `ProfilesGitUrl` — a neutral project glyph.

The stored `color`, when present, SHALL paint the small accent dot beside
the mark (registry, detail) and the tag chips' tint; it SHALL NOT paint
chrome (buttons, links, focus rings) — those stay on theme tokens. When
absent, the dot and chips fall back to the muted ink of the surrounding
data surface. The colour is decoration, never the only carrier of
meaning: slug and name always identify the row alongside it.

#### Scenario: Derived mark for a GitHub-hosted project

- **WHEN** the registry renders a project with no stored icon and
  `ProfilesGitUrl = "git@github.com:org/worker-profiles.git"`
- **THEN** the identity cell shows the GitHub mark

#### Scenario: Stored icon wins over derivation

- **WHEN** a project has `icon = "🛰️"` and a GitHub profiles URL
- **THEN** the identity cell shows the emoji, not the GitHub mark

#### Scenario: Accent dot follows the stored colour

- **WHEN** a project has `color = "#3c5a86"`
- **THEN** its registry row and detail identity block show an accent dot
  in that colour, and other rows' dots stay on the fallback ink

### Requirement: Tag filtering in the registry

The projects registry toolbar SHALL offer tag filtering over the loaded
list: a multi-select of the tags present across visible projects;
selecting one or more tags narrows the table to rows carrying **all**
selected tags (AND semantics). Filtering SHALL run client-side over the
full list the registry already loaded — the list endpoint gains no new
query parameters for tags. The tag filter composes with the existing
text filter (both must match) and the shown-count reflects the narrowed
set.

#### Scenario: Filter by one tag

- **WHEN** the operator selects the tag `billing`
- **THEN** only rows whose tag list contains `billing` remain, and the
  shown count equals that number of rows

#### Scenario: Two tags intersect

- **WHEN** the operator selects `billing` and `web` and exactly one
  project carries both
- **THEN** the table shows that one project only

#### Scenario: Clearing the tag filter

- **WHEN** the operator clears the tag selection
- **THEN** the table returns to the previous text-filter-only state

## MODIFIED Requirements

### Requirement: Slug is immutable

The slug is the stable external key other modules reference. Update SHALL be partial (PATCH semantics — null fields leave stored values untouched) over name, description, profiles git URL, profiles git ref, icon, color and tags (absent tags keep the stored list; an empty array clears it); the slug SHALL NOT be editable through any endpoint.

#### Scenario: Rename without slug change

- **WHEN** a caller patches only the name
- **THEN** the name changes and every other field, including the slug, stays

#### Scenario: Patch identity only

- **WHEN** a caller patches `icon`, `color` and `tags` and nothing else
- **THEN** those fields change and the name, slug and git fields stay

### Requirement: REST surface

Projects SHALL be served under `/api/v1/projects`: `POST /` (201 + view; 409 slug conflict; 400 validation), `GET /` (`?includeArchived`), `GET /{projectId}` (404 unknown), `PATCH /{projectId}`, `DELETE /{projectId}` (archive, 204), `GET /{projectId}/settings`, `PUT /{projectId}/settings` (409 version conflict). The create and PATCH bodies SHALL also accept the optional identity fields `icon`, `color` and `tags`, and every project view SHALL expose `icon`, `color` and `tags` (icon/colour nullable, tags a possibly-empty array) alongside the existing fields. Typed exceptions become ProblemDetails in one place; validation failures answer 400 with per-field errors. Unknown project ids answer 404 with the projectId extension.

#### Scenario: Unknown project

- **WHEN** any project endpoint is called with an id that does not exist
- **THEN** the answer is 404 `Project not found` with the requested id in the extensions

#### Scenario: View carries identity fields

- **WHEN** a caller GETs a project created before this change
- **THEN** the view answers `icon: null`, `color: null` and `tags: []`

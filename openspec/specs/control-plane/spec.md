# Control Plane Specification

## Purpose

Defines the versioned-as-code control-plane content: worker profiles, built-in chat commands, and skills as markdown documents with a YAML-ish frontmatter subset; the parsing semantics shared by the C# catalog and the TS readers; and the read-only catalog API. The control plane is text content, not code — defaults live in the repo, client overlays live in the client's git and are pinned per run.

## Requirements

### Requirement: Document format

Every control-plane document SHALL be markdown with a frontmatter block fenced by `---` lines. The frontmatter SHALL support a YAML subset: `key: value` scalars (ASCII keys), flow lists (`[a, b]`), block lists (`- item` under an empty value) and `#` comments; nested structures and tags are ignored. Quotes around values are stripped. Required keys per kind:

- profiles: `name` (non-empty), `description` (non-empty); optional `allowedTools` (list; a scalar degrades to a single-item list), `model` (scalar role hint)
- chat commands: `name` (non-empty), `description` (non-empty)
- skills (`SKILL.md`): `name` (non-empty), `description`; optional `scope` (scalar or list)

The document key SHALL be the file stem (e.g. `explore-readonly`); for skills, the directory name.

#### Scenario: Block-list allowedTools
- **WHEN** a profile declares `allowedTools:` followed by `- Read` / `- Bash` lines
- **THEN** the parsed allow-list is `[Read, Bash]`

#### Scenario: Missing closing fence
- **WHEN** a document opens with `---` but never closes the frontmatter
- **THEN** the parse returns null (no document, no throw)

### Requirement: Malformed documents are skipped, never fatal

Listing many documents SHALL NOT throw on one malformed entry: a document with no frontmatter, no closing fence, or missing/empty name or description yields null and the catalog skips it with a warning naming the file. One broken skill must not hide the rest of the catalog.

#### Scenario: One bad profile among many
- **WHEN** `profiles/` contains nine valid documents and one without frontmatter
- **THEN** the catalog lists nine and logs a skip warning for the tenth

### Requirement: Profile catalog

The catalog SHALL read every `.md` directly under `<control-plane root>/profiles/` (top directory only), returning entries ordered by key with `Key`, `Name`, `Description`, `AllowedTools` (empty when unrestricted) and advisory `Model`. `GetAsync(key)` SHALL match case-insensitively and return null for unknown keys. The profile's system-prompt BODY is deliberately NOT part of the catalog-facing shape — prompts are consumed by workers/agents, not served through the catalog API.

#### Scenario: Profile body stays private
- **WHEN** a client lists profiles
- **THEN** the response carries metadata only — no prompt body

### Requirement: Chat command catalog

The catalog SHALL read every `.md` under `<control-plane root>/chat-commands/`, returning entries ordered by key with `Key`, `Name`, `Description` and `Body` (the markdown instructions the brain follows). Command keys are the slash-command names (the default pack: `init`, `run`, `status`, `stop`, `plan`, `project`, `help`).

#### Scenario: Command body is exposed
- **WHEN** a chat harness lists commands
- **THEN** each entry includes the instruction body it renders

### Requirement: Root resolution

The control-plane root SHALL be taken from configuration when set (compose: a mounted client overlay; tests: a temp dir); otherwise the catalog probes upward from the application base directory (bounded depth 8) for a directory named `control-plane` — the dev-checkout case. A root that cannot be resolved yields an empty catalog with a warning, not a boot failure.

#### Scenario: Dev checkout probe
- **WHEN** the host runs from its build output inside a repo checkout without configured root
- **THEN** the catalog finds the repo's `control-plane/` by walking up

### Requirement: Catalog API endpoints

The catalog SHALL be exposed as: `GET /profiles` (list), `GET /profiles/{key}` (200 or 404 for unknown keys), `GET /chat-commands` (list). `GET /profiles` and `/profiles/{key}` SHALL demand the `plan:read` permission; `GET /chat-commands` SHALL demand `chat:use` (enforcement per identity: 401 anonymous, 403 `permission.denied`). Skills, worker rules and any other control-plane folders SHALL NOT be exposed through this API.

#### Scenario: Unknown profile key
- **WHEN** `GET /profiles/does-not-exist` is called by a subject holding `plan:read`
- **THEN** the answer is 404 with no body

#### Scenario: Skills not on the API
- **WHEN** any skills or rules path is requested
- **THEN** no catalog endpoint serves it (skills are loaded by the worker SDK from the mounted content)

### Requirement: Parser parity C# ↔ TS

The C# catalog parser and the TS rule-doc reader in agent-core SHALL implement the same scalar/list subset with the same tolerance, so both sides read identical content from the same mounted control-plane (see agents-sdk).

#### Scenario: Same document, both readers
- **WHEN** one profile document is parsed by the C# catalog and the TS reader
- **THEN** both agree on name, description and list fields

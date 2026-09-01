# Agents SDK Specification

## Purpose

Defines the TypeScript packages that ride the two agent runtimes: `comuki-agent-core` (shared by worker and dev SDKs — event types, brief/report protocol, rule-doc reading) and `comuki-worker-sdk` (lock descriptors and matching, skills loading). The dev SDK (`comuki-dev-sdk`, Claude Code runtime) is scaffolded with no landed behavior yet.

## Requirements

### Requirement: Bun workspace layout

The agents tree SHALL be a bun workspace whose members live in `comuki-*` directories (the workspace glob) under `agents/`, with a committed lockfile pinned despite the repo-level bun.lock ignore, so worker images and CI resolve identical dependency versions. Package names are `@comuki/agent-core`, `@comuki/worker-sdk`, `@comuki/dev-sdk`.

#### Scenario: Workspace membership
- **WHEN** a new package lands under `agents/`
- **THEN** it must be named `comuki-*` to be part of the workspace build and test set

### Requirement: Pi event mirror (zod)

`@comuki/agent-core` SHALL mirror the C# pi-event discriminated union as zod schemas discriminated by `kind`: `system`, `user`, `assistant-text`, `assistant-tool-use`, `result`, `unknown`, `unparseable`. Field names SHALL be the camelCase forms the C# parser emits, so both sides of the Translator seam agree on one shape.

#### Scenario: Same union, both languages
- **WHEN** a stream-json line is parsed by the C# parser and the TS parser
- **THEN** both yield the semantically identical event kind and fields

### Requirement: Tolerant line parsing

`parsePiLine` SHALL return null for blank lines; malformed JSON or a missing/non-string `type` field yields an `unparseable` event (line + error); known event types map to their typed records with tolerant field access (missing/invalid fields fall back to defaults); unmodelled types yield `unknown` carrying the type and raw payload. A single bad line never throws. `parsePiStream` SHALL apply the same rules to a full dump, skipping blanks.

#### Scenario: Bad JSON is an event, not a crash
- **WHEN** `parsePiLine` receives `{not json`
- **THEN** it returns `{ kind: 'unparseable', line, error }` without throwing

#### Scenario: Unknown future event preserved
- **WHEN** pi emits a new event type the mirror does not model
- **THEN** it surfaces as `unknown` with the raw JSON intact

### Requirement: Brief protocol

A brief (the work order the orchestrator hands a worker) SHALL validate as: `taskId` (non-empty string), `profileKey` (non-empty), `prompt` (non-empty), optional `contextFiles` (array of non-empty strings) and optional `rulesDigest`. Parsing an untrusted payload SHALL throw `ZodError` on mismatch.

#### Scenario: Brief rejects empties
- **WHEN** a brief arrives with an empty `profileKey`
- **THEN** `parseBrief` throws a ZodError naming the field

### Requirement: Report protocol

A report (the worker's final answer) SHALL validate as: `status` ∈ `succeeded` | `failed` | `cancelled`, `summary` (string), optional `artifacts` (array of non-empty strings). Parsing throws `ZodError` on mismatch. These DTOs mirror the C# side until codegen lands.

#### Scenario: Invalid status rejected
- **WHEN** a report carries status `"done"`
- **THEN** `parseReport` throws

### Requirement: Rule-doc reader with frontmatter

`parseRuleDoc` SHALL parse markdown with a `---`-fenced frontmatter carrying `name` (min 1 char), `description`, optional `scope` (scalar or list), using the same YAML subset as the C# parser (flow lists, block lists, comments, quote stripping). A document with no frontmatter block or an invalid name/description SHALL return null — never throw — so listing many documents survives one malformed entry.

#### Scenario: Malformed rule skipped
- **WHEN** a rules directory contains a doc whose frontmatter lacks `name`
- **THEN** `parseRuleDoc` returns null and the caller skips it

### Requirement: Lock descriptors

A lock SHALL be pure data: `{ id, kind, pattern, reason }` with kind ∈ `edit-path` | `tool-name` | `git-ref`. Locks describe restrictions; enforcement lives in runtime adapters (pi-extensions for workers, Claude Code hooks for devs) — both adapters share this vocabulary.

- `edit-path` matches file paths the agent wants to edit (glob, path separators normalized to `/`)
- `tool-name` matches tool calls rendered as `Tool(<primary-arg-prefix>)`, e.g. `Bash(npm install left-pad)` (glob with segment-stars disabled)
- `git-ref` matches the full git ref, e.g. `refs/heads/main`

A rule SHALL only match the subject of its own kind — an `edit-path` rule never blocks a tool call. `find*` helpers return the first matching rule so a blocker can explain itself with the human-readable reason.

#### Scenario: Kind isolation
- **WHEN** an `edit-path` lock with pattern `**/tests/**` is tested against the tool call `Bash(dotnet test)`
- **THEN** it does not match

### Requirement: Default lock set

Every worker SHALL start with the fixed default lock set before profile extensions: no editing test files (`**/*.test.ts(x)`, `**/*.spec.ts(x)`, `**/tests/**`, `**/__tests__/**`), no side-effect dependency installs (`Bash(npm install*`, `bun add*`, `pnpm add*`, `yarn add*`, `pip install*`, `dotnet add package*`), no direct pushes to protected branches (`refs/heads/main`, `refs/heads/master`). Each lock carries its reason (tests are owned by the platform gate; installs change the locked environment; results land via the orchestrator).

#### Scenario: Worker cannot edit its own gate
- **WHEN** a worker attempts to edit `src/app.test.ts`
- **THEN** the default `edit-path` lock matches with the platform-gate reason

### Requirement: Skills loader

The skills loader SHALL read `<skills-root>/<skill-name>/SKILL.md` — markdown with valid `name`/`description` frontmatter (the same rule-doc format). A skill whose SKILL.md or frontmatter is missing/invalid SHALL be skipped (null), not fatal — one broken skill must not hide the rest. `listSkills` SHALL return valid skills sorted by name, each with name, description, body and directory name.

#### Scenario: Broken skill skipped
- **WHEN** one skills directory contains a SKILL.md without frontmatter
- **THEN** `listSkills` returns the other skills and omits it

## ADAPTER Notes

The dev SDK (`comuki-dev-sdk`) and the core MCP client (`src/mcp/`) are scaffolded placeholders — no landed behavior to spec yet. Enforcement wiring of locks into the pi runtime (pi-extensions) is likewise pending.

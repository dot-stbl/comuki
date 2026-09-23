## ADDED Requirements

### Requirement: Locks are enforced in the pi runtime
Default and profile locks SHALL be enforced by a pi-extension (or equivalent pi hook) at tool-call time. Files on disk describing locks SHALL NOT be treated as enforcement. A matching lock SHALL deny the tool call and surface the lock reason.

#### Scenario: Test-file edit is denied at runtime
- **WHEN** pi attempts to edit `src/app.test.ts`
- **THEN** the pi-extension denies the call with the platform-gate reason before the write

### Requirement: Skills are loaded into pi
Skills materialized under the working directory SHALL be registered with pi through the worker SDK extension so the agent can invoke them. A directory of `SKILL.md` files without the extension SHALL NOT count as available skills.

#### Scenario: Listed skill is callable
- **WHEN** prepare has copied a valid skill into the skills root and the extension is loaded
- **THEN** pi can invoke that skill by name

### Requirement: MCP reaches pi only through the worker SDK
If `COMUKI_MCP_URL` is set on the pi process, the worker SDK SHALL expose MCP tools to pi. The Claude Code / dev-sdk MCP client SHALL NOT be the worker path. Egress to the MCP host is allowed only when that host is on the effective allowlist.

#### Scenario: No URL means no MCP tools
- **WHEN** `COMUKI_MCP_URL` is unset
- **THEN** pi has no Comuki MCP tools

## MODIFIED Requirements

### Requirement: Skills loader
The skills loader SHALL read `<skills-root>/<skill-name>/SKILL.md` — markdown with valid `name`/`description` frontmatter (the same rule-doc format). A skill whose SKILL.md or frontmatter is missing/invalid SHALL be skipped (null), not fatal — one broken skill must not hide the rest. `listSkills` SHALL return valid skills sorted by name, each with name, description, body and directory name. Loading into the pi runtime is a separate requirement (pi-extension); the loader alone does not make a skill callable.

#### Scenario: Broken skill skipped
- **WHEN** one skills directory contains a SKILL.md without frontmatter
- **THEN** `listSkills` returns the other skills and omits it

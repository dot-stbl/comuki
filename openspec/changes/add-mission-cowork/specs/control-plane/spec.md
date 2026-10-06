## MODIFIED Requirements

### Requirement: Skill metadata — trigger_when / validate_against / version
A `SKILL.md` frontmatter SHALL accept, on top of today's `name` / `description` / `scope` keys, the following optional managed-asset metadata:

- `trigger_when` (string or flow list) — a natural-language statement of when the skill should apply. The Brain reads `trigger_when` as a hint, never as an instruction: a skill is selected by the Brain on its own judgement, with `trigger_when` narrowing the search space. The catalog SHALL NOT auto-select a skill solely because `trigger_when` matched a query string.
- `validate_against` (string or flow list) — one or more control-plane documents (paths, URLs, or `SourceRef` kind + id) that the skill's applicability depends on. When the catalog detects that any `validate_against` target has changed generation since the skill was last validated, the catalog marks the skill as `stale` and the Brain's tool surface reports the skill as `stale` in its selection; the Brain may still apply the skill, but its stale state is visible in the operation trace.
- `version` (semver, `<major>.<minor>.<patch>`) — the skill's declared version. Defaults to `0.1.0` if absent. The `version` is the human-facing identity; the control-plane generation (per `Control plane generation watermarks`, the file's commit SHA) is the runtime identity. A skill without `version` is still consumable, but its identity is ambiguous across revisions.

A skill missing any of `trigger_when` / `validate_against` is treated as "no metadata" by the catalog; today the catalog already tolerates missing optional keys, so this is a strict superset of existing behaviour. The Brain / Worker SDK's skill loader SHALL surface the same three fields through the `IComukiSkill` (or platform-equivalent) view; the catalog list response SHALL include `trigger_when`, `validate_against`, and `version` per skill alongside the existing `Key`, `Name`, `Description`, `Scope`.

#### Scenario: Skill with full metadata
- **WHEN** a `SKILL.md` declares
  ```yaml
  trigger_when: "drafting a long-form document that needs citation cleanup"
  validate_against:
    - "../../rules/citation-format.md"
    - { kind: knowledge, id: "doc/citation-style@v3" }
  version: "1.2.0"
  ```
- **THEN** the catalog lists this skill with `TriggerWhen = ["drafting a long-form document that needs citation cleanup"]`, `ValidateAgainst = [Citation-format rule, citation-style doc generation v3]`, `Version = "1.2.0"`. The Brain's tool surface includes the skill with the same fields visible.

#### Scenario: Validate-against target changes generation
- **WHEN** a skill's `validate_against` names a Knowledge source at `revision=v3` and the Knowledge corpus rolls forward to `revision=v4`
- **THEN** the catalog marks the skill `stale=true`; the Brain's operation trace records the stale state at the moment the skill was selected; the skill itself is still applied (the Brain, not the catalog, decides); the catalog records the stale transition with the target's new revision.

#### Scenario: Skill without metadata stays simple
- **WHEN** a `SKILL.md` carries only `name` and `description`
- **THEN** the catalog lists it with empty `TriggerWhen` and empty `ValidateAgainst` and `Version = "0.1.0"`; the Brain treats it exactly as today (manual selection only). The behavior for "no metadata" is identical to today — this requirement is a strict superset.

#### Scenario: Trigger_when does not auto-select
- **WHEN** the planner evaluates candidate skills for a query whose token set overlaps a skill's `trigger_when`
- **THEN** the catalog returns the skill as a candidate with `trigger_when` visible to the Brain; the Brain's own model decides whether to apply it. The catalog SHALL NOT auto-include the skill solely because of a text overlap.
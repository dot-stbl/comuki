## MODIFIED Requirements

### Requirement: Default lock set
Every worker SHALL start with the fixed default lock set before profile extensions: no editing test files (`**/*.test.ts(x)`, `**/*.spec.ts(x)`, `**/tests/**`, `**/__tests__/**`), no side-effect dependency installs (`Bash(npm install*`, `bun add*`, `pnpm add*`, `yarn add*`, `pip install*`, `dotnet add package*`), no direct pushes to protected branches (`refs/heads/main`, `refs/heads/master`). Each lock carries its reason (tests are owned by the platform gate; installs change the locked environment; results land via the orchestrator). Host-side restore opcodes declared by the environment class (`dotnet restore`, `bun install` in listed directories) SHALL run in the Translator prepare step, not as agent Bash, and SHALL NOT be granted to the coding agent as a way around this lock set.

#### Scenario: Worker cannot edit its own gate
- **WHEN** a worker attempts to edit `src/app.test.ts`
- **THEN** the default `edit-path` lock matches with the platform-gate reason

#### Scenario: Agent cannot add a package during implement
- **WHEN** the coding agent invokes `dotnet add package`
- **THEN** the default install lock matches and the call is denied

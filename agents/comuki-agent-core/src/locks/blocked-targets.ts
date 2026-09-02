import type { LockRule } from './lock-rule';

const TESTS_OWNED_BY_PLATFORM =
  'Test files are owned by the platform gate; editing them invalidates the verification signal.';
const NO_SIDE_INSTALLS =
  'Dependency installs change the locked environment; declare the dependency in the brief instead.';
const NO_DIRECT_PUSH =
  'Agents never push directly to a protected branch; results land via the orchestrator.';

/**
 * The default lock set every Comuki agent starts with — workers (pi) and
 * devs (Claude Code) share the same semantics; only the enforcement mechanics
 * differ (pi-extensions vs Claude Code hooks). Small and fixed by design
 * (see agents/README.md — the lock set is deliberately tiny so duplicating
 * the *mechanics* stays cheap while the *semantics* live here, once).
 */
export const BLOCKED_TOOL_TARGETS: readonly LockRule[] = [
  { id: 'no-edit-tests', kind: 'edit-path', pattern: '**/*.test.*', reason: TESTS_OWNED_BY_PLATFORM },
  { id: 'no-edit-spec', kind: 'edit-path', pattern: '**/*.spec.*', reason: TESTS_OWNED_BY_PLATFORM },
  { id: 'no-edit-tests-dir', kind: 'edit-path', pattern: '**/tests/**', reason: TESTS_OWNED_BY_PLATFORM },
  { id: 'no-edit-underscore-tests-dir', kind: 'edit-path', pattern: '**/__tests__/**', reason: TESTS_OWNED_BY_PLATFORM },
  { id: 'no-npm-install', kind: 'tool-name', pattern: 'Bash(npm install*', reason: NO_SIDE_INSTALLS },
  { id: 'no-npm-add', kind: 'tool-name', pattern: 'Bash(npm add*', reason: NO_SIDE_INSTALLS },
  { id: 'no-bun-add', kind: 'tool-name', pattern: 'Bash(bun add*', reason: NO_SIDE_INSTALLS },
  { id: 'no-bun-install', kind: 'tool-name', pattern: 'Bash(bun install*', reason: NO_SIDE_INSTALLS },
  { id: 'no-pnpm-add', kind: 'tool-name', pattern: 'Bash(pnpm add*', reason: NO_SIDE_INSTALLS },
  { id: 'no-pnpm-install', kind: 'tool-name', pattern: 'Bash(pnpm install*', reason: NO_SIDE_INSTALLS },
  { id: 'no-yarn-add', kind: 'tool-name', pattern: 'Bash(yarn add*', reason: NO_SIDE_INSTALLS },
  { id: 'no-yarn-install', kind: 'tool-name', pattern: 'Bash(yarn install*', reason: NO_SIDE_INSTALLS },
  { id: 'no-pip-install', kind: 'tool-name', pattern: 'Bash(pip install*', reason: NO_SIDE_INSTALLS },
  { id: 'no-dotnet-add-package', kind: 'tool-name', pattern: 'Bash(dotnet add package*', reason: NO_SIDE_INSTALLS },
  { id: 'no-push-main', kind: 'git-ref', pattern: 'refs/heads/main', reason: NO_DIRECT_PUSH },
  { id: 'no-push-master', kind: 'git-ref', pattern: 'refs/heads/master', reason: NO_DIRECT_PUSH },
];

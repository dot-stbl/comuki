import type { LockRule } from './lock-rule';

const TESTS_OWNED_BY_PLATFORM =
  'Test files are owned by the platform gate; a worker editing them invalidates the verification signal.';
const NO_SIDE_INSTALLS =
  'Dependency installs change the locked environment; declare the dependency in the brief instead.';
const NO_DIRECT_PUSH =
  'Workers never push directly to a protected branch; results land via the orchestrator.';

/**
 * The default lock set every worker starts with, before profile-specific
 * extensions. Small and fixed by design (see agents/README.md — "Замки
 * дублируются дважды, но это маленький фиксированный набор"). Enforcement
 * wiring lands with the pi-extensions adapter (T12.3 hook-up).
 */
export const BLOCKED_TOOL_TARGETS: readonly LockRule[] = [
  { id: 'no-edit-tests-ts', kind: 'edit-path', pattern: '**/*.test.ts', reason: TESTS_OWNED_BY_PLATFORM },
  { id: 'no-edit-tests-tsx', kind: 'edit-path', pattern: '**/*.test.tsx', reason: TESTS_OWNED_BY_PLATFORM },
  { id: 'no-edit-spec-ts', kind: 'edit-path', pattern: '**/*.spec.ts', reason: TESTS_OWNED_BY_PLATFORM },
  { id: 'no-edit-spec-tsx', kind: 'edit-path', pattern: '**/*.spec.tsx', reason: TESTS_OWNED_BY_PLATFORM },
  { id: 'no-edit-tests-dir', kind: 'edit-path', pattern: '**/tests/**', reason: TESTS_OWNED_BY_PLATFORM },
  { id: 'no-edit-underscore-tests-dir', kind: 'edit-path', pattern: '**/__tests__/**', reason: TESTS_OWNED_BY_PLATFORM },
  { id: 'no-npm-install', kind: 'tool-name', pattern: 'Bash(npm install*', reason: NO_SIDE_INSTALLS },
  { id: 'no-bun-add', kind: 'tool-name', pattern: 'Bash(bun add*', reason: NO_SIDE_INSTALLS },
  { id: 'no-pnpm-add', kind: 'tool-name', pattern: 'Bash(pnpm add*', reason: NO_SIDE_INSTALLS },
  { id: 'no-yarn-add', kind: 'tool-name', pattern: 'Bash(yarn add*', reason: NO_SIDE_INSTALLS },
  { id: 'no-pip-install', kind: 'tool-name', pattern: 'Bash(pip install*', reason: NO_SIDE_INSTALLS },
  { id: 'no-dotnet-add-package', kind: 'tool-name', pattern: 'Bash(dotnet add package*', reason: NO_SIDE_INSTALLS },
  { id: 'no-push-main', kind: 'git-ref', pattern: 'refs/heads/main', reason: NO_DIRECT_PUSH },
  { id: 'no-push-master', kind: 'git-ref', pattern: 'refs/heads/master', reason: NO_DIRECT_PUSH },
];

import { describe, expect, test } from 'bun:test';
import { decideLockGate, emitDecision, gitPushRefCandidates, hookStyleFromEnv, parseHookPayload, runLockGate } from './lock-gate';

function hookJson(tool_name: string, tool_input: Record<string, unknown>): string {
  return JSON.stringify({ session_id: 's1', hook_event_name: 'PreToolUse', tool_name, tool_input });
}

describe('decideLockGate — edit-path locks', () => {
  test('denies editing test files in place', () => {
    const decision = decideLockGate(parseHookPayload(hookJson('Edit', { file_path: 'src/foo.test.ts', old_string: 'a', new_string: 'b' })));

    expect(decision.decision).toBe('deny');
    expect(decision.ruleId).toBe('no-edit-tests');
  });

  test('denies writing spec files and nested test dirs', () => {
    expect(decideLockGate(parseHookPayload(hookJson('Write', { file_path: 'deep/pkg/foo.spec.tsx', content: '' }))).decision).toBe('deny');
    expect(decideLockGate(parseHookPayload(hookJson('Edit', { file_path: 'tests/unit/parser.test.js' }))).decision).toBe('deny');
    expect(decideLockGate(parseHookPayload(hookJson('Edit', { file_path: 'src/__tests__/helper.ts' }))).decision).toBe('deny');
  });

  test('denies NotebookEdit via notebook_path', () => {
    expect(decideLockGate(parseHookPayload(hookJson('NotebookEdit', { notebook_path: 'notes/tests.ipynb' }))).decision).toBe('allow');
    expect(decideLockGate(parseHookPayload(hookJson('NotebookEdit', { notebook_path: 'tests/notes.ipynb' }))).decision).toBe('deny');
  });

  test('allows editing regular source files', () => {
    expect(decideLockGate(parseHookPayload(hookJson('Edit', { file_path: 'src/foo.ts' }))).decision).toBe('allow');
  });

  test('normalizes windows separators in file paths', () => {
    expect(decideLockGate(parseHookPayload(hookJson('Edit', { file_path: 'src\\deep\\foo.test.ts' }))).decision).toBe('deny');
  });
});

describe('decideLockGate — tool-name locks (installs)', () => {
  const installCommands = [
    'npm install zod',
    'npm add zod',
    'bun add @types/bun',
    'bun install',
    'pnpm add left-pad',
    'pnpm install',
    'yarn add left-pad',
    'yarn install',
    'pip install requests',
    'dotnet add package Newtonsoft.Json',
  ];

  test.each(installCommands)('denies "%s"', (command) => {
    expect(decideLockGate(parseHookPayload(hookJson('Bash', { command }))).decision).toBe('deny');
  });

  test('allows ordinary package-manager scripts', () => {
    expect(decideLockGate(parseHookPayload(hookJson('Bash', { command: 'npm run build' }))).decision).toBe('allow');
    expect(decideLockGate(parseHookPayload(hookJson('Bash', { command: 'bun test' }))).decision).toBe('allow');
  });
});

describe('gitPushRefCandidates', () => {
  test('extracts the ref after the remote', () => {
    expect(gitPushRefCandidates('git push origin main')).toEqual(['main', 'refs/heads/main']);
  });

  test('treats a single positional as the refspec', () => {
    expect(gitPushRefCandidates('git push main')).toEqual(['main', 'refs/heads/main']);
  });

  test('unwraps src:dst refspecs to the destination', () => {
    expect(gitPushRefCandidates('git push origin HEAD:main')).toContain('refs/heads/main');
  });

  test('keeps fully-qualified refs as-is', () => {
    expect(gitPushRefCandidates('git push origin refs/heads/master')).toContain('refs/heads/master');
  });

  test('skips flags, flag values and global git flags', () => {
    expect(gitPushRefCandidates('git push --force origin main')).toContain('refs/heads/main');
    expect(gitPushRefCandidates('git -C sub push origin main')).toContain('refs/heads/main');
    expect(gitPushRefCandidates('git push --repo=upstream origin main')).toContain('refs/heads/main');
  });

  test('ignores non-push git commands', () => {
    expect(gitPushRefCandidates('git commit -m push')).toEqual([]);
    expect(gitPushRefCandidates('git status')).toEqual([]);
    expect(gitPushRefCandidates('docker push main')).toEqual([]);
  });
});

describe('decideLockGate — git-ref locks (push to protected branches)', () => {
  const protectedPushes = [
    'git push origin main',
    'git push origin master',
    'git push origin HEAD:main',
    'git push --force origin main',
    'git -C packages/api push origin main',
    'cd packages/api && git push origin main',
    'git push origin refs/heads/main',
  ];

  test.each(protectedPushes)('denies "%s"', (command) => {
    const decision = decideLockGate(parseHookPayload(hookJson('Bash', { command })));

    expect(decision.decision).toBe('deny');
    expect(decision.ruleId !== undefined && ['no-push-main', 'no-push-master'].includes(decision.ruleId)).toBe(true);
  });

  test('allows pushes to feature branches and non-push mentions', () => {
    expect(decideLockGate(parseHookPayload(hookJson('Bash', { command: 'git push origin feature/dev-sdk' }))).decision).toBe('allow');
    expect(decideLockGate(parseHookPayload(hookJson('Bash', { command: 'git commit -m "push main now"' }))).decision).toBe('allow');
  });
});

describe('decideLockGate — fail-open on unknown shapes', () => {
  test('allows on malformed json', () => {
    expect(decideLockGate(parseHookPayload('not json at all')).decision).toBe('allow');
  });

  test('allows on non-object json', () => {
    expect(decideLockGate(parseHookPayload('[1,2,3]')).decision).toBe('allow');
  });

  test('allows unknown tools', () => {
    expect(decideLockGate(parseHookPayload(hookJson('Read', { file_path: 'src/foo.test.ts' }))).decision).toBe('allow');
  });

  test('allows Bash without a command string', () => {
    expect(decideLockGate(parseHookPayload(hookJson('Bash', { command: 42 }))).decision).toBe('allow');
  });
});

describe('emitDecision — both output shapes behind one emitter', () => {
  const denyGate = { decision: 'deny' as const, reason: 'tests are platform-owned', ruleId: 'no-edit-tests' };
  const allowGate = { decision: 'allow' as const, reason: 'no lock matched' };

  test('json style: deny emits hookSpecificOutput JSON with exit 0', () => {
    const output = emitDecision(denyGate, 'json');

    expect(output.exitCode).toBe(0);
    expect(output.stderr).toBe('');
    const parsed = JSON.parse(output.stdout) as {
      hookSpecificOutput: { hookEventName: string; permissionDecision: string; permissionDecisionReason: string };
    };
    expect(parsed.hookSpecificOutput.hookEventName).toBe('PreToolUse');
    expect(parsed.hookSpecificOutput.permissionDecision).toBe('deny');
    expect(parsed.hookSpecificOutput.permissionDecisionReason).toBe('tests are platform-owned');
  });

  test('json style: allow is silent', () => {
    const output = emitDecision(allowGate, 'json');

    expect(output).toEqual({ exitCode: 0, stdout: '', stderr: '' });
  });

  test('exit style: deny uses exit code 2 + stderr reason', () => {
    const output = emitDecision(denyGate, 'exit');

    expect(output.exitCode).toBe(2);
    expect(output.stderr).toBe('tests are platform-owned');
    expect(output.stdout).toBe('');
  });

  test('exit style: allow is exit 0 and silent', () => {
    expect(emitDecision(allowGate, 'exit')).toEqual({ exitCode: 0, stdout: '', stderr: '' });
  });
});

describe('runLockGate — stdin fixtures end to end', () => {
  test('denied edit produces json decision by default', () => {
    const output = runLockGate(hookJson('Edit', { file_path: 'src/app.test.ts' }));

    expect(output.exitCode).toBe(0);
    expect(() => JSON.parse(output.stdout)).not.toThrow();
  });

  test('COMUKI_HOOK_STYLE=exit switches the shape', () => {
    const output = runLockGate(hookJson('Edit', { file_path: 'src/app.test.ts' }), { COMUKI_HOOK_STYLE: 'exit' });

    expect(output.exitCode).toBe(2);
    expect(output.stderr.length).toBeGreaterThan(0);
  });

  test('empty env keeps json default', () => {
    const output = runLockGate(hookJson('Edit', { file_path: 'src/app.ts' }), {});

    expect(output).toEqual({ exitCode: 0, stdout: '', stderr: '' });
  });

  test('hookStyleFromEnv accepts only exit as override', () => {
    expect(hookStyleFromEnv(undefined)).toBe('json');
    expect(hookStyleFromEnv('json')).toBe('json');
    expect(hookStyleFromEnv('exit')).toBe('exit');
    expect(hookStyleFromEnv('bogus')).toBe('json');
  });
});

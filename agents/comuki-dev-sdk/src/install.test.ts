import { afterAll, describe, expect, test } from 'bun:test';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import {
  applyHooks,
  backupPath,
  buildHookDefs,
  installHooks,
  isOurCommand,
  removeHooks,
  uninstallHooks,
} from './install';
import type { ClaudeSettings, HookCommandDef } from './install';

const tempDirs: string[] = [];

afterAll(() => {
  for (const dir of tempDirs) {
    rmSync(dir, { recursive: true, force: true });
  }
});

function tempSettings(initial: string): string {
  const dir = mkdtempSync(join(tmpdir(), 'comuki-install-'));
  tempDirs.push(dir);
  const path = join(dir, 'settings.json');
  writeFileSync(path, initial, 'utf8');
  return path;
}

const PACKAGE_DIR = 'C:/repo/agents/comuki-dev-sdk';
const DEFS: readonly HookCommandDef[] = buildHookDefs(PACKAGE_DIR);

describe('applyHooks', () => {
  test('installs both hook events into empty settings', () => {
    const next = applyHooks({}, DEFS);

    expect(Object.keys(next.hooks ?? {}).sort()).toEqual(['PreToolUse', 'SessionStart']);
    expect(next.hooks?.PreToolUse?.[0]?.matcher).toBe('Bash|Edit|Write|MultiEdit|NotebookEdit');
    expect(next.hooks?.PreToolUse?.[0]?.hooks?.[0]?.command).toContain('lock-gate.ts');
    expect(next.hooks?.SessionStart?.[0]?.matcher).toBe('*');
    expect(next.hooks?.SessionStart?.[0]?.hooks?.[0]?.command).toContain('comuki-context.ts');
  });

  test('is idempotent — applying twice yields the identical document', () => {
    const once = applyHooks({}, DEFS);
    const twice = applyHooks(once, DEFS);

    expect(twice).toEqual(once);
  });

  test('preserves unrelated hooks and settings keys', () => {
    const existing: ClaudeSettings = {
      model: 'opus',
      hooks: {
        PreToolUse: [
          {
            matcher: 'Bash',
            hooks: [{ type: 'command', command: 'other-guard --check', timeout: 5 }],
          },
        ],
      },
    };

    const next = applyHooks(existing, DEFS);
    const preGroups = next.hooks?.PreToolUse ?? [];

    expect(preGroups.length).toBe(2);
    expect(preGroups[0]?.hooks?.[0]?.command).toBe('other-guard --check');
    expect(preGroups.find((group) => group.matcher === 'Bash|Edit|Write|MultiEdit|NotebookEdit')).toBeDefined();
    expect(next.model).toBe('opus');
  });

  test('reuses an existing matcher group instead of duplicating it', () => {
    const existing: ClaudeSettings = {
      hooks: {
        SessionStart: [{ matcher: '*', hooks: [{ type: 'command', command: 'echo hi' }] }],
      },
    };

    const next = applyHooks(existing, DEFS);
    const group = next.hooks?.SessionStart?.[0];
    const sessionDef = DEFS[1];

    expect(next.hooks?.SessionStart?.length).toBe(1);
    expect(group?.hooks?.map((entry) => entry.command)).toEqual(['echo hi', sessionDef?.command ?? '<missing def>']);
  });

  test('does not mutate the input settings', () => {
    const existing: ClaudeSettings = { hooks: {} };

    applyHooks(existing, DEFS);

    expect(existing).toEqual({ hooks: {} });
  });
});

describe('isOurCommand / removeHooks', () => {
  test('detects our commands across path separator styles', () => {
    expect(isOurCommand(`bun "${PACKAGE_DIR}/src/hooks/lock-gate.ts"`, PACKAGE_DIR)).toBe(true);
    expect(isOurCommand('bun "C:\\repo\\agents\\comuki-dev-sdk\\src\\hooks\\lock-gate.ts"', PACKAGE_DIR)).toBe(true);
    expect(isOurCommand('other-guard --check', PACKAGE_DIR)).toBe(false);
  });

  test('removes our entries and keeps foreign ones', () => {
    const installed = applyHooks(
      {
        hooks: {
          PreToolUse: [{ matcher: 'Bash', hooks: [{ type: 'command', command: 'other-guard --check' }] }],
          SessionStart: [{ matcher: '*', hooks: [{ type: 'command', command: 'echo hi' }] }],
        },
      },
      DEFS,
    );

    const cleaned = removeHooks(installed, PACKAGE_DIR);

    expect(cleaned.hooks?.PreToolUse?.length).toBe(1);
    expect(cleaned.hooks?.PreToolUse?.[0]?.hooks?.[0]?.command).toBe('other-guard --check');
    expect(cleaned.hooks?.SessionStart?.[0]?.hooks?.[0]?.command).toBe('echo hi');
  });

  test('prunes empty groups and the empty hooks key', () => {
    const cleaned = removeHooks(applyHooks({}, DEFS), PACKAGE_DIR);

    expect(cleaned.hooks).toBeUndefined();
  });
});

describe('installHooks / uninstallHooks — file level', () => {
  test('first install writes settings and backs up the original once', async () => {
    const settingsPath = tempSettings('{\n  "model": "opus"\n}\n');

    const first = await installHooks({ settingsPath, packageDir: PACKAGE_DIR });

    expect(first.changed).toBe(true);
    expect(first.backupPath).toBe(backupPath(settingsPath));
    expect(JSON.parse(readFileSync(backupPath(settingsPath), 'utf8'))).toEqual({ model: 'opus' });

    const onDisk = JSON.parse(readFileSync(settingsPath, 'utf8')) as ClaudeSettings;
    expect(onDisk.model).toBe('opus');
    expect(onDisk.hooks?.PreToolUse).toBeDefined();
  });

  test('second install is a no-op and does not touch the backup', async () => {
    const settingsPath = tempSettings('{}');
    await installHooks({ settingsPath, packageDir: PACKAGE_DIR });
    const backupBefore = readFileSync(backupPath(settingsPath), 'utf8');
    const settingsBefore = readFileSync(settingsPath, 'utf8');

    const second = await installHooks({ settingsPath, packageDir: PACKAGE_DIR });

    expect(second.changed).toBe(false);
    expect(second.backupPath).toBeNull();
    expect(readFileSync(settingsPath, 'utf8')).toBe(settingsBefore);
    expect(readFileSync(backupPath(settingsPath), 'utf8')).toBe(backupBefore);
  });

  test('install into a missing settings file creates it without a backup', async () => {
    const dir = mkdtempSync(join(tmpdir(), 'comuki-install-'));
    tempDirs.push(dir);
    const settingsPath = join(dir, 'nested', 'settings.json');

    const result = await installHooks({ settingsPath, packageDir: PACKAGE_DIR });

    expect(result.changed).toBe(true);
    expect(result.backupPath).toBeNull();
    expect(existsSync(backupPath(settingsPath))).toBe(false);
    expect(JSON.parse(readFileSync(settingsPath, 'utf8')).hooks).toBeDefined();
  });

  test('uninstall restores a foreign-only settings document', async () => {
    const original = JSON.stringify({
      hooks: {
        PreToolUse: [{ matcher: 'Bash', hooks: [{ type: 'command', command: 'other-guard --check' }] }],
      },
    });
    const settingsPath = tempSettings(original);
    await installHooks({ settingsPath, packageDir: PACKAGE_DIR });

    const removed = await uninstallHooks({ settingsPath, packageDir: PACKAGE_DIR });

    expect(removed.changed).toBe(true);
    expect(JSON.parse(readFileSync(settingsPath, 'utf8'))).toEqual(JSON.parse(original));
  });

  test('uninstall on untouched settings is a no-op', async () => {
    const settingsPath = tempSettings('{"model":"opus"}');

    const removed = await uninstallHooks({ settingsPath, packageDir: PACKAGE_DIR });

    expect(removed.changed).toBe(false);
  });

  test('refuses to touch unreadable settings instead of clobbering', async () => {
    const settingsPath = tempSettings('{ broken json');

    await expect(installHooks({ settingsPath, packageDir: PACKAGE_DIR })).rejects.toThrow(/unreadable settings/);
    expect(readFileSync(settingsPath, 'utf8')).toBe('{ broken json');
  });
});

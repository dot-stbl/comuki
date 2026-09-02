/**
 * Claude Code settings patcher — wires the Comuki hooks (lock gate +
 * session context) into the user's `.claude/settings.json`.
 *
 * - idempotent: applying twice yields the identical file (merge by exact
 *   hook command within the matching matcher group);
 * - backs up once: the first modification copies the pristine settings to
 *   `<settings>.comuki-backup.json`;
 * - `--uninstall` removes exactly our entries and leaves everything else
 *   (other hooks, permissions, model config) untouched.
 *
 * CLI: `bun src/install.ts [--uninstall] [--settings <path>]`
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { homedir } from 'node:os';

export type HookEventName = 'PreToolUse' | 'SessionStart';

export interface HookCommandDef {
  readonly event: HookEventName;
  readonly matcher: string;
  readonly command: string;
  readonly timeout: number;
}

export interface HookCommandEntry {
  readonly type: 'command';
  readonly command: string;
  readonly timeout?: number;
}

export interface HookMatcherGroup {
  readonly matcher?: string;
  readonly hooks?: HookCommandEntry[];
}

export interface ClaudeSettings {
  readonly hooks?: Record<string, HookMatcherGroup[]>;
  readonly [key: string]: unknown;
}

export const DEFAULT_SETTINGS_PATH = () => join(homedir(), '.claude', 'settings.json');

export function backupPath(settingsPath: string): string {
  return `${settingsPath}.comuki-backup.json`;
}

/** The hook commands this package installs, with absolute script paths. */
export function buildHookDefs(packageDir: string): HookCommandDef[] {
  return [
    {
      event: 'PreToolUse',
      matcher: 'Bash|Edit|Write|MultiEdit|NotebookEdit',
      command: `bun "${join(packageDir, 'src', 'hooks', 'lock-gate.ts')}"`,
      timeout: 15,
    },
    {
      event: 'SessionStart',
      matcher: '*',
      command: `bun "${join(packageDir, 'src', 'hooks', 'comuki-context.ts')}"`,
      timeout: 10,
    },
  ];
}

/** True when a hook command is one of ours (path-based, separator-agnostic). */
export function isOurCommand(command: string, packageDir: string): boolean {
  const normalizedCommand = command.replaceAll('\\', '/');
  const normalizedPackageDir = packageDir.replaceAll('\\', '/').replace(/\/+$/, '');
  return normalizedCommand.includes(`${normalizedPackageDir}/src/hooks/`);
}

/** Pure merge — idempotent by construction; never touches unrelated entries. */
export function applyHooks(settings: ClaudeSettings, defs: readonly HookCommandDef[]): ClaudeSettings {
  const next = structuredClone(settings) as MutableClaudeSettings;
  next.hooks ??= {};

  for (const def of defs) {
    const groups = (next.hooks[def.event] ??= []);
    const entry: MutableHookCommandEntry = { type: 'command', command: def.command, timeout: def.timeout };

    const group = groups.find((candidate) => candidate.matcher === def.matcher);
    if (group === undefined) {
      groups.push({ matcher: def.matcher, hooks: [entry] });
      continue;
    }
    group.hooks ??= [];
    if (!group.hooks.some((existing) => existing.command === def.command)) {
      group.hooks.push(entry);
    }
  }

  return next;
}

/** Pure removal — drops our entries, prunes empty groups and empty `hooks`. */
export function removeHooks(settings: ClaudeSettings, packageDir: string): ClaudeSettings {
  const next = structuredClone(settings) as MutableClaudeSettings;
  if (next.hooks === undefined) {
    return next;
  }

  for (const eventName of Object.keys(next.hooks)) {
    const groups = next.hooks[eventName];
    if (groups === undefined) {
      continue;
    }
    const keptGroups = groups
      .map((group) => ({ ...group, hooks: (group.hooks ?? []).filter((entry) => !isOurCommand(entry.command, packageDir)) }))
      .filter((group) => (group.hooks ?? []).length > 0);
    if (keptGroups.length === 0) {
      delete next.hooks[eventName];
    } else {
      next.hooks[eventName] = keptGroups;
    }
  }

  if (Object.keys(next.hooks).length === 0) {
    delete next.hooks;
  }
  return next;
}

export interface InstallResult {
  readonly changed: boolean;
  readonly backupPath: string | null;
}

export async function installHooks(options: {
  readonly settingsPath: string;
  readonly packageDir: string;
  readonly defs?: readonly HookCommandDef[];
}): Promise<InstallResult> {
  const current = readSettingsOrEmpty(options.settingsPath);
  const next = applyHooks(current, options.defs ?? buildHookDefs(options.packageDir));
  if (JSON.stringify(next) === JSON.stringify(current)) {
    return { changed: false, backupPath: null };
  }
  return persistWithBackup(options.settingsPath, current, next);
}

export async function uninstallHooks(options: {
  readonly settingsPath: string;
  readonly packageDir: string;
}): Promise<InstallResult> {
  const current = readSettingsOrEmpty(options.settingsPath);
  const next = removeHooks(current, options.packageDir);
  if (JSON.stringify(next) === JSON.stringify(current)) {
    return { changed: false, backupPath: null };
  }
  return persistWithBackup(options.settingsPath, current, next);
}

function persistWithBackup(settingsPath: string, current: ClaudeSettings, next: ClaudeSettings): InstallResult {
  const backup = backupPath(settingsPath);
  let backedUp: string | null = null;
  if (existsSync(settingsPath) && !existsSync(backup)) {
    writeFileSync(backup, readFileSync(settingsPath), 'utf8');
    backedUp = backup;
  }
  mkdirSync(dirname(settingsPath), { recursive: true });
  writeFileSync(settingsPath, `${JSON.stringify(next, null, 2)}\n`, 'utf8');
  return { changed: true, backupPath: backedUp };
}

function readSettingsOrEmpty(settingsPath: string): ClaudeSettings {
  if (!existsSync(settingsPath)) {
    return {};
  }
  const text = readFileSync(settingsPath, 'utf8');
  if (text.trim().length === 0) {
    return {};
  }
  try {
    const parsed: unknown = JSON.parse(text);
    if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
      throw new Error('settings root is not an object');
    }
    return parsed as ClaudeSettings;
  } catch (exception) {
    throw new Error(
      `refusing to touch unreadable settings at ${settingsPath}: ${(exception as Error).message}`,
      { cause: exception },
    );
  }
}

interface MutableHookCommandEntry {
  type: 'command';
  command: string;
  timeout?: number;
}

interface MutableHookMatcherGroup {
  matcher?: string;
  hooks?: MutableHookCommandEntry[];
}

interface MutableClaudeSettings {
  hooks?: Record<string, MutableHookMatcherGroup[]>;
  [key: string]: unknown;
}

if (import.meta.main) {
  const args = process.argv.slice(2);
  const uninstall = args.includes('--uninstall');
  const settingsFlagIndex = args.indexOf('--settings');
  const settingsPath =
    settingsFlagIndex !== -1 ? (args[settingsFlagIndex + 1] ?? DEFAULT_SETTINGS_PATH()) : DEFAULT_SETTINGS_PATH();
  const packageDir = dirname(import.meta.dir);

  const result = uninstall
    ? await uninstallHooks({ settingsPath, packageDir })
    : await installHooks({ settingsPath, packageDir });

  if (!result.changed) {
    process.stdout.write(uninstall ? 'comuki hooks: nothing to remove\n' : 'comuki hooks: already installed\n');
  } else {
    process.stdout.write(
      `${uninstall ? 'removed' : 'installed'} comuki hooks in ${settingsPath}` +
        (result.backupPath === null ? '' : ` (backup: ${result.backupPath})`) +
        '\n',
    );
  }
}

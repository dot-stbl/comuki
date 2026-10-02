#!/usr/bin/env node
/** Pour GitHub master onto GitLab master. Overlay stays on GitLab. Never force. */
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, rmSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
export const overlayPaths = ['.gitlab-ci.yml', 'deploy/hybrid'];

export function parseArgs(argv) {
  const options = {
    apply: false,
    help: false,
    source: 'github/master',
    target: 'gitlab/master',
    pushRemote: 'gitlab',
    pushRef: 'master',
    repoRoot: REPO_ROOT,
    worktree: undefined,
    stripOverlay: false,
    worktreeSlug: 'sync-gitlab',
  };
  for (let i = 0; i < argv.length; i += 1) {
    const raw = argv[i];
    if (raw === '--help' || raw === '-h') {
      options.help = true;
      continue;
    }
    if (raw === '--apply') {
      options.apply = true;
      continue;
    }
    if (raw === '--dry-run') {
      options.apply = false;
      continue;
    }
    if (raw === '--to-github') {
      options.source = 'gitlab/master';
      options.target = 'github/master';
      options.pushRemote = 'github';
      options.pushRef = 'master';
      options.stripOverlay = true;
      options.worktreeSlug = 'sync-github';
      continue;
    }
    const eq = raw.indexOf('=');
    const flag = eq === -1 ? raw : raw.slice(0, eq);
    const inline = eq === -1 ? undefined : raw.slice(eq + 1);
    const take = () => {
      if (inline !== undefined) {
        return inline;
      }
      const next = argv[i + 1];
      if (next === undefined || next.startsWith('--')) {
        return undefined;
      }
      i += 1;
      return next;
    };
    const map = {
      '--source': 'source',
      '--target': 'target',
      '--push-remote': 'pushRemote',
      '--push-ref': 'pushRef',
      '--repo': 'repoRoot',
      '--worktree': 'worktree',
    };
    if (!map[flag]) {
      return { ok: false, error: `unrecognized argument: ${raw}` };
    }
    const value = take();
    if (!value) {
      return { ok: false, error: `${flag} needs a value` };
    }
    options[map[flag]] = flag === '--repo' || flag === '--worktree' ? resolve(value) : value;
  }
  options.worktree ??= resolve(options.repoRoot, '.agents/worktree', options.worktreeSlug);
  return { ok: true, options };
}

export function classifyPour({ sourceAhead, targetAhead }) {
  if (sourceAhead === 0 && targetAhead === 0) {
    return 'identical';
  }
  if (sourceAhead > 0 && targetAhead === 0) {
    return 'fast-forward';
  }
  if (sourceAhead === 0 && targetAhead > 0) {
    return 'catch-up-target';
  }
  return 'diverged';
}

export function pourAction(kind) {
  if (kind === 'identical') {
    return 'noop';
  }
  if (kind === 'catch-up-target') {
    return 'noop-warn-github-behind';
  }
  return 'merge-source-into-target';
}

export function overlayTouched(paths, overlay = overlayPaths) {
  return paths.some((path) => overlay.some((p) => path === p || path.startsWith(`${p}/`)));
}

export function restoreOverlayCommitMessage() {
  return '[.stbl](feat/meta): restore hybrid overlay after github pour';
}

export function stripOverlayCommitMessage() {
  return '[.stbl](feat/meta): drop hybrid overlay from github tip';
}

function git(repo, args, extra = {}) {
  const result = spawnSync('git', args, {
    cwd: extra.cwd ?? repo,
    encoding: 'utf8',
    stdio: extra.stdio ?? ['ignore', 'pipe', 'pipe'],
  });
  return { status: result.status ?? 1, stdout: result.stdout ?? '', stderr: result.stderr ?? '' };
}

function must(result, label) {
  if (result.status === 0) {
    return;
  }
  throw new Error(`${label} failed: ${(result.stderr || result.stdout).trim()}`);
}

function counts(stdout) {
  const match = stdout.trim().match(/^(\d+)\s+(\d+)$/);
  if (!match) {
    throw new Error(`bad rev-list output: ${JSON.stringify(stdout)}`);
  }
  return { sourceAhead: Number(match[1]), targetAhead: Number(match[2]) };
}

export function planPour(options, deps = {}) {
  const run = deps.git ?? git;
  const { repoRoot, source, target } = options;
  const [sr, ...sb] = source.split('/');
  const [tr, ...tb] = target.split('/');
  must(run(repoRoot, ['fetch', sr, sb.join('/')]), `fetch ${source}`);
  must(run(repoRoot, ['fetch', tr, tb.join('/')]), `fetch ${target}`);
  const raw = run(repoRoot, ['rev-list', '--left-right', '--count', `${source}...${target}`]);
  must(raw, 'rev-list');
  const c = counts(raw.stdout);
  const kind = classifyPour(c);
  const srcSha = run(repoRoot, ['rev-parse', '--short', source]);
  const tgtSha = run(repoRoot, ['rev-parse', '--short', target]);
  must(srcSha, 'rev-parse source');
  must(tgtSha, 'rev-parse target');
  const log = (range) => {
    const r = run(repoRoot, ['log', '--format=%h %s', range]);
    return r.status === 0 ? r.stdout.split(/\r?\n/).map((l) => l.trim()).filter(Boolean) : [];
  };
  return {
    source,
    target,
    sourceSha: srcSha.stdout.trim(),
    targetSha: tgtSha.stdout.trim(),
    ...c,
    kind,
    action: pourAction(kind),
    overlayPaths: [...overlayPaths],
    sourceUnique: log(`${target}..${source}`),
    targetUnique: log(`${source}..${target}`),
    push: `${options.pushRemote} ${options.pushRef}`,
    stripOverlay: Boolean(options.stripOverlay),
    force: false,
  };
}

export function renderPlan(plan) {
  const lines = [
    `pour ${plan.source} (${plan.sourceSha}) → ${plan.target} (${plan.targetSha})`,
    `kind: ${plan.kind}  action: ${plan.action}  force: never`,
    `ahead: source +${plan.sourceAhead}  target +${plan.targetAhead}`,
    plan.stripOverlay
      ? `overlay stripped before push to ${plan.target}: ${plan.overlayPaths.join(', ')}`
      : `overlay kept from ${plan.target}: ${plan.overlayPaths.join(', ')}`,
    `push: ${plan.push}`,
  ];
  if (plan.sourceUnique.length) {
    lines.push('github unique:', ...plan.sourceUnique.slice(0, 20).map((s) => `  ${s}`));
  }
  if (plan.targetUnique.length) {
    lines.push(
      plan.stripOverlay ? 'github unique (kept on github tip):' : 'gitlab unique (kept; github is behind):',
      ...plan.targetUnique.slice(0, 12).map((s) => `  ${s}`),
      plan.stripOverlay
        ? 'catch-up: gitlab code lands on github, overlay is dropped.'
        : 'warn: catch GitHub up separately if it should stay the code tip.',
    );
  }
  if (plan.action.startsWith('noop')) {
    lines.push(plan.stripOverlay ? 'nothing to pour onto GitHub.' : 'nothing to pour onto GitLab.');
  }
  return `${lines.join('\n')}\n`;
}

function applyPour(options, deps = {}) {
  const run = deps.git ?? git;
  const { repoRoot, worktree, source, target, pushRemote, pushRef } = options;
  if (existsSync(worktree)) {
    rmSync(worktree, { recursive: true, force: true });
  }
  mkdirSync(dirname(worktree), { recursive: true });
  must(run(repoRoot, ['worktree', 'add', '--detach', worktree, target]), 'worktree add');
  const merge = run(repoRoot, ['merge', '--no-edit', source], { cwd: worktree });
  if (merge.status !== 0) {
    throw new Error(`merge failed in ${worktree}\n${merge.stderr || merge.stdout}`);
  }
  if (options.stripOverlay) {
    must(run(repoRoot, ['rm', '-r', '--ignore-unmatch', '--', ...overlayPaths], { cwd: worktree }), 'strip overlay');
    const dirty = run(repoRoot, ['status', '--porcelain', '--', ...overlayPaths], { cwd: worktree });
    must(dirty, 'status overlay');
    if (dirty.stdout.trim()) {
      must(run(repoRoot, ['commit', '-m', stripOverlayCommitMessage()], { cwd: worktree }), 'commit overlay strip');
    }
  } else {
    must(run(repoRoot, ['checkout', target, '--', ...overlayPaths], { cwd: worktree }), 'restore overlay');
    const dirty = run(repoRoot, ['status', '--porcelain', '--', ...overlayPaths], { cwd: worktree });
    must(dirty, 'status overlay');
    if (dirty.stdout.trim()) {
      must(run(repoRoot, ['add', '--', ...overlayPaths], { cwd: worktree }), 'add overlay');
      must(run(repoRoot, ['commit', '-m', restoreOverlayCommitMessage()], { cwd: worktree }), 'commit overlay');
    }
  }
  must(run(repoRoot, ['push', pushRemote, `HEAD:refs/heads/${pushRef}`], { cwd: worktree }), 'push');
  run(repoRoot, ['worktree', 'remove', '--force', worktree]);
  return { worktree, pushed: `${pushRemote}/${pushRef}` };
}

export function main(argv, deps = {}) {
  const parsed = parseArgs(argv.slice(2));
  if (!parsed.ok) {
    process.stderr.write(`${parsed.error}\n`);
    process.exitCode = 2;
    return 2;
  }
  if (parsed.options.help) {
    process.stdout.write(
      'Pour GitHub master onto GitLab master (hybrid overlay stays on GitLab).\n',
    );
    return 0;
  }
  try {
    const plan = planPour(parsed.options, deps);
    process.stdout.write(renderPlan(plan));
    if (!parsed.options.apply) {
      process.stdout.write(
        parsed.options.stripOverlay
          ? 'dry-run. pass --apply to merge gitlab onto github + strip overlay + push github.\n'
          : 'dry-run. pass --apply to merge + restore overlay + push gitlab.\n',
      );
      return 0;
    }
    if (plan.action.startsWith('noop')) {
      return 0;
    }
    const result = applyPour(parsed.options, deps);
    process.stdout.write(`pushed ${result.pushed} from ${result.worktree}\n`);
    return 0;
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : error}\n`);
    process.exitCode = 1;
    return 1;
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main(process.argv);
}

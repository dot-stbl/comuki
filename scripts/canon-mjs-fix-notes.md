# canon.mjs directory-expansion fix — U1

## Context

`canon.mjs` lives at the user-global path
`~/.agents/skills/canon/scripts/canon.mjs` and is **outside this repo**.
It is a separate git-tracked file under `~/.agents/...` and cannot be
committed from this worktree. The fix has been applied in place; this
note documents the diff so any future setup can reproduce it (e.g. on a
fresh machine, when re-installing the skill, or when reviewing the
change).

## Bug

`node ~/.agents/skills/canon/scripts/canon.mjs prepare --paths platform/src/modules --json`
treated the directory as a literal file, so `scope.files` became
`['platform/src/modules']` (a directory), no extensions could be
classified, `languages` came back empty (`[]`), and 32 csharp rules
with `always: true` were skipped with the reason
`"always:true but no csharp files in scope"`.

## Fix

A small helper `expandPathsToFileLeaves(repo, inputs)` runs at the top
of the `--paths` branch in `resolveScope`. For each input:

- If it points to a directory on disk → run
  `git ls-files -z -- <rel>` and keep entries matching
  `/\.(cs|ts|tsx)$/i`. `-z` (null-separated) is used so paths with
  spaces or unicode are not mangled by line-splitting.
- If it points to a file → keep as-is.
- If it doesn't exist on disk → keep as-is (existing `existingFiles`
  filter then drops it and the `empty scope` error fires as before).

Backward compatibility is preserved: `--paths a.cs,b.ts` continues to
work exactly as before because each entry resolves to an existing file
and the `else` branch returns it unchanged.

## Diff (vs the pre-fix version)

### Change 1 — add the leaf regex next to other module-level constants

After line 32 (`const PREFIX_SPLIT = ...`), insert:

```js
const SOURCE_LEAF_RE = /\.(cs|ts|tsx)$/i;
```

### Change 2 — call the helper in the `--paths` branch

In `resolveScope`, replace:

```js
if (flags.paths) {
  const files = flags.paths.map((item) => toPosix(item));
  return { kind: 'paths', ref: null, files: existingFiles(repo, files) };
}
```

with:

```js
if (flags.paths) {
  const files = expandPathsToFileLeaves(repo, flags.paths);
  return { kind: 'paths', ref: null, files: existingFiles(repo, files) };
}
```

### Change 3 — add the helper itself

Insert immediately after `existingFiles`:

```js
function expandPathsToFileLeaves(repo, inputs) {
  const out = [];
  for (const raw of inputs) {
    const item = toPosix(raw);
    const abs = path.isAbsolute(item) ? item : path.join(repo, ...item.split('/'));
    let stat;
    try {
      stat = statSync(abs);
    } catch {
      out.push(item);
      continue;
    }
    if (stat.isDirectory()) {
      const relDir = toPosix(path.relative(repo, abs));
      const result = git(repo, ['ls-files', '-z', '--', relDir]);
      if (result.status !== 0) {
        out.push(item);
        continue;
      }
      for (const file of result.stdout.split('\0')) {
        const trimmed = file.trim();
        if (trimmed && SOURCE_LEAF_RE.test(trimmed)) {
          out.push(toPosix(trimmed));
        }
      }
      continue;
    }
    out.push(item);
  }
  return out;
}
```

`statSync` is already imported at the top of the file
(`import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';`),
so no new imports are needed.

## Why not use `gitLines`?

`gitLines` (line 205) splits on `\r?\n`. `git ls-files -z` emits null-separated
output, so `gitLines` would read it as a single concatenated line and
return garbage. Using `git` directly with a null-split keeps the helper
self-contained and avoids changing `gitLines`'s semantics, which other
callers depend on.

## Verification commands

Run from the worktree root. Each must exit 0 with non-empty `matched`.

```bash
node ~/.agents/skills/canon/scripts/canon.mjs prepare --paths platform/src/modules --json
# scope.fileCount: 599   languages: ['csharp']   matched: 60

node ~/.agents/skills/canon/scripts/canon.mjs prepare --paths platform/src/shared --json
# scope.fileCount: 145   languages: ['csharp']   matched: 58

node ~/.agents/skills/canon/scripts/canon.mjs prepare --paths platform/src/host,platform/src/engine --json
# scope.fileCount: 351   languages: ['csharp']   matched: 62

node ~/.agents/skills/canon/scripts/canon.mjs prepare --paths platform/src/host/Comuki.Host/HostComposer.cs --json
# scope.fileCount: 1     languages: ['csharp']   matched: 58  (backward-compat: single file)
```

Before the fix: `prepare --paths platform/src/modules` returned
`scope.fileCount: 1, languages: [], matched: 20, skipped: 53` (32 of those
skips for the csharp-language miss).

After the fix: `scope.fileCount: 599, languages: ['csharp'], matched: 60,
skipped: 13`. All 32 always-skipped csharp rules flip to matched.

## Branch + commit

This note is committed on branch
`fix/canon-mjs-directory-expansion`. The canonical script lives outside
the repo and is changed in place at
`~/.agents/skills/canon/scripts/canon.mjs`.

Parent commit on this branch (just this file):

```
[hybrid](fix/meta/canon): document directory-expansion fix for canon.mjs

canon.mjs lives at ~/.agents/skills/canon/scripts/canon.mjs — outside
this repo, so the script change itself is applied in place and cannot
be committed here. This note records the diff so a future agent can
reproduce the fix on a fresh machine.

Bug: --paths <dir> was treated as a literal file; languages came back
empty and always:true csharp rules were skipped.
Fix: helper expandPathsToFileLeaves runs `git ls-files -z` on each
directory argument and keeps .cs/.ts/.tsx leaves.

Verified: prepare --paths platform/src/modules now returns 599 files,
languages=['csharp'], matched=60 (was 1/[]/20).
```

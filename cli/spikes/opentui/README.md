# cli/spikes/opentui — throwaway OpenTUI spike for CLI v1

> **This is throwaway code. Not a production dependency.**
> It exists only to produce the evidence in
> [.agents/docs/architecture/adr-0002-cli-opentui.md](../../../.agents/docs/architecture/adr-0002-cli-opentui.md)
> and [evidence/react-windows.md](./evidence/react-windows.md).
> Delete this directory once the ADR is signed and the production
> CLI host has landed in `cli/src/`.

## What it is

A Bun + TypeScript spike that mounts an OpenTUI chat shell against
`@opentui/core@0.5.11` + `@opentui/keymap@0.5.11`, then drives it
through the issue's acceptance criteria. Used to choose between
OpenTUI Core and OpenTUI React for the v1 CLI presentation stack.

The spike ships only the Core chat shell. The React chat shell is
quarantined in `evidence/react-windows.md` (deferred on Windows for
this spike; see ADR §B and §"Отклонённые альтернативы").

## Layout

```
cli/spikes/opentui/
├── bin/opentui-spike.ts             # host entry point
├── src/
│   ├── commands/registry.ts         # @opentui/keymap-backed command/keymap surface
│   ├── core/chat-shell.ts           # the OpenTUI Core host (production direction)
│   └── fixtures/transcript-1000.ts   # 1,000-entry focus-mode fixture
├── tests/
│   ├── commands.test.ts             # palette/keymap shared surface
│   ├── approval.test.ts             # medium-risk approval card in captureCharFrame
│   ├── resize.test.ts               # 160x50 → 80x24 → 48x16 with same draft
│   └── suspend.test.ts              # editor suspend / restore
├── scripts/                         # evidence / measurement scripts
└── evidence/
    └── react-windows.md             # React decision deferred
```

## Commands

```sh
cd cli/spikes/opentui

# install
bun install

# spike-local gates (run from this directory)
bun run typecheck
bun run test:core        # 19 pass / 0 fail — the spike-local gate
bun run test:core        # run twice — must stay green

# evidence scripts
bun run smoke:load
bun run measure:cold-start
bun run measure:fixture
bun run measure:native-stats
bun run measure:idle-memory   # 5-second window, NOT a 30-min claim
bun run measure:standalone    # builds + reports size/hash, leaves binary behind
```

The `measure:*` scripts do not all complete under a second — the
`measure:idle-memory` script holds a **5-second** idle window by
design. The 5 s window is a deliberate trade-off (ADR §"Доказательства");
anything beyond that is dominated by GC scheduling. The
`measure:standalone` script leaves `comuki-opentui-spike.exe` in the
spike directory; remove it with `rm comuki-opentui-spike.exe` (or
run `bun run measure:standalone` again and `rm` after).

## What's NOT here

- **No React source.** `src/react/chat-shell.tsx` is gone (deleted
  in iteration 6). `tests/react.test.ts` is gone. `package.json`
  has no `@opentui/react`, no `react`, no `react-reconciler`,
  no `@types/react`. The spike is Core-only on purpose — the
  React decision is documented in `evidence/react-windows.md`.
- **No production CLI changes.** Nothing in `cli/src/` is touched
  by this spike.
- **No generated binaries in git.** `comuki-opentui-spike[.exe]` is
  ignored by the spike's local `.gitignore` (the file
  `cli/spikes/opentui/.gitignore`). The standalone script leaves
  the binary on disk for inspection — `rm comuki-opentui-spike.exe`
  cleans up. SHA-256 is recorded in the ADR so the build is
  reproducible.
- **No 30-minute idle-memory claim.** The `measure:idle-memory`
  script holds a 5-second idle window. Anything beyond that is
  dominated by GC scheduling, not the spike.
- **No CI integration yet.** `bun run test:core` is a spike-local
  gate; the repo's CI does not yet run this directory. When CI is
  wired, this script becomes the gate command.

## Tested environment

The spike ran on this worktree's host:

| Item | Version |
| --- | --- |
| OS | Windows x64 |
| Bun | 1.3.10 |
| TypeScript | 5.9.3 |
| `@opentui/core` | 0.5.11 |
| `@opentui/keymap` | 0.5.11 |

Linux x64, macOS, SSH, tmux, screen-reader, manual TTY sessions are
**not tested** by this spike. The ADR marks them UNVERIFIED.

See [.agents/docs/architecture/adr-0002-cli-opentui.md §5](../../../.agents/docs/architecture/adr-0002-cli-opentui.md)
for the platform matrix and the re-open triggers.

## Throwaway contract

When the v1 CLI host lands in `cli/src/` (strangler migration per
ADR §9), this directory is deleted. Until then: do not add new
dependencies, do not change the fixture shape without re-running
`bun run test:core`, do not relax acceptance assertions to make a
flaky test pass — fix the underlying issue or document it in the ADR.

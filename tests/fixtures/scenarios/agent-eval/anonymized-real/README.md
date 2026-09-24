# `anonymized-real/` — documented slot for human-reviewed real-ticket scenarios

This folder is the documented landing pad for an "anonymized real
tickets" subset of the WS10 corpus, per `openspec/changes/add-agentic-test-contour/design.md`'s open
question on how to source real-world ticket text without leaking
customer/company identifiers.

## Intended file shape

Any scenario landing here MUST follow the same shape as the synthetic
entries in the parent directory:

```
schemaVersion: 1
name: <kebab-case-unique>
description: ...
ticket:
  source: github
  title: ...
  body: ...
  labels: [...]
  targetRepo:
    fixture: small-repo
    ref: main
worker:
  image: comuki-agent-test-worker:ws6
  profileKey: implement
  profilesRef: test
model:
  mode: fake
  fakeScript: scripts/<name>.fake.json
eval:
  difficulty: <trivial|easy|medium|hard>
  expectedOutcome: ...
  allowedFiles: [...]
  maxToolCalls: <n>
  rubric:                # optional
    criteria: [...]
    minScore: <0..1>
```

Plus a paired `scripts/<name>.fake.json` next to it (same shape as
the parent directory's fake scripts).

## Anonymization requirements — hard gate

Every field that may have originated from a real customer ticket MUST
be scrubbed before commit:

- `ticket.title` and `ticket.body`: replace company names, product
  names, internal project codenames, person names, and ticket IDs with
  generic placeholders ("Customer A", "their API", "the orders
  service", "the user", "ORDER-123").
- `eval.expectedOutcome`: same rules — no customer / company / product
  names.
- The repo name `comuki` and the test fixture `small-repo` are fine to
  keep — they are public codebase identifiers, not customer data.
- No real URLs, no real workspace paths, no real internal Slack
  threads, no real log lines.

## Review checklist (PR gate)

A change that adds a scenario under this folder MUST include an
explicit PR-checklist item confirming the author has reviewed the
anonymization rules above for every field, with no exceptions. A
reviewer must verify the checklist is checked before merge. This is a
human gate, not an automated one — `CorpusLoader.LoadDirectory` does
not enforce it.

## Why this folder is empty today

No real tickets have been sourced/anonymized/reviewed for this
workstream yet — the synthetic entries in the parent directory cover
the same eval surface (diff-applies, files-within-allowed-set,
tool-call-count, cost-from-usage, tests-pass, optional rubric scoring)
without the anonymization overhead. This folder ships as a
documented slot and process only; first real content lands in a
follow-up change once a candidate ticket is identified and
anonymization-review passes.

## How `CorpusLoader` interacts with this folder

`CorpusLoader.LoadDirectory` globs `*.scenario.yaml` / `*.scenario.json`
recursively. This folder currently contains no such files, so the
recursive glob naturally finds zero entries here — no special-case
skip is required. If a contributor adds a scenario file without
going through the anonymization-review PR gate, `CorpusLoader` WILL
pick it up and treat it as a real corpus entry — the gate is a human
PR review, not a tool check.

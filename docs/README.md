# Comuki — `docs/`

Committed, human-readable documentation for the platform itself
(distinct from the design-system specs in `docs/design-system/`).

## Layout

```
docs/
├── README.md                   ← you are here
└── design-system/              ← tokens / components / patterns / voice
                                  (Phase 3 input; see docs/design-system/README.md)
```

## Why separate from `.soly/docs/`

`.soly/docs/` holds **intent documents** — written *before* any plans
exist, defining the business/architectural vision that soly reads
on every session. They are reference material for the agent, not
project source.

`docs/` is **project source** — committed alongside code, versioned
with code, consumed by other tools (lint rules, codegen, future
worker prompts). When the design system docs arrive here, they're
the contract that code is graded against.

## Future

- `runbooks/` — operational guides ("how to roll out a new model",
  "how to debug a stuck worker") — Phase 8.
- `architecture/decisions.md` — running ADR log — Phase 8 or later,
  once the system has actual decisions worth recording.
- Anything else worth committing, e.g. integration specs with
  external systems (Linear, Nexus, etc.).

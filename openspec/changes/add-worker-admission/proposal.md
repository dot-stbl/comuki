## Why

Five gates already exist on five changes: env class, sandbox isolation, secret inject, edition, control-plane profile. None compose. Translator and `StartAsync` each grow another `if`. A Community fleet can start a GPU class with unfenced egress and no secrets policy. The missing contract is one **admission record** per slot — over-structured on purpose: one deny, one code, one journal, instead of five silent skips.

## What Changes

- Introduce `SlotAdmission`: the only input `IComputeProvider.StartAsync` and the Translator spawn path accept for a coding-agent execution. It carries profile, env class, isolation class, secret refs, edition decision, target repository, and the four behavior axes (role / toolchain / product rules / fleet).
- **BREAKING** for start: a worker container SHALL NOT start, and pi SHALL NOT spawn, without a successful admission. Failures are typed (`admission.env_unconfirmed`, `admission.capacity`, `admission.edition`, `admission.sandbox`, `admission.secrets`, `admission.publisher`) — never a generic 500 and never `blocked-external` for missing GPU.
- Editions gain a fifth enforcement point: the ephemeral coding-agent slot (distinct from `IComukiWorker` hosted services).
- Worker-pools advertisement includes `env_class` (not only `images`). Hosts that do not advertise the admitted class SHALL NOT receive the slot.
- Document the four in-repo/fleet axes so they cannot collapse back into the worker image: profile (role, control-plane), env class (toolchain, `.comuki`), product rules (target git), fleet (catalog allowlist + isolation).

Does not re-specify clone, secrets catalog, sandbox fence, or env toml — those stay on their changes. Admission *calls* them.

## Capabilities

### New Capabilities

- `worker-admission`: SlotAdmission shape, ordered evaluation, typed denials, journal `worker.admitted` / `worker.admission_denied`, composition with sibling changes.

### Modified Capabilities

- `compute`: `StartAsync` requires an admission id / record; class+isolation+secrets come from it, not from ad-hoc env.
- `worker-runtime`: Translator loop step 0 is admission; spawn is forbidden on deny.
- `editions`: fifth gate — coding-agent slot — via the same `EditionGate` function, not a sixth copy.
- `runs`: journal types `worker.admitted` and `worker.admission_denied`.

## Impact

Compute start request, Translator prepare, Host composition, journal types, edition registry (new feature keys for Windows/GPU/community bundles if paid). Depends on `add-worker-environments` (class), `harden-pi-worker-sandbox` (isolation floor), `agent-runtime-capabilities` (secret refs), `add-mission-cowork` worker-pools (when slots exist; until then admission still wraps 1:1 containers).

## Non-goals

- Replacing Capability Broker (that's semantic commands for humans/Brain). Admission is the *compute* chokepoint for a slot.
- Implementing clone, restore, mint-key, or bundle bake here.
- A generic policy engine / OPA. Ordered typed checks, one record.
- Changing personal chat or Mission membership.

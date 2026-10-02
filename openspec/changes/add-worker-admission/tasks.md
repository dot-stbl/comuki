## 1. Admission port

- [ ] 1.1 Add `SlotAdmission` record + `ISlotAdmissionEvaluator` with the six ordered checks (class, publisher, capacity, isolation, edition, secrets); unimplemented siblings return pass with a documented hook. Verify: unit tests for `env_unconfirmed` and `capacity` without calling Docker.
- [ ] 1.2 Journal `worker.admitted` / `worker.admission_denied` with typed codes, no secret values. Verify: journal payload tests.
- [ ] 1.3 `StartAsync` requires admission id; conflicting image fails. Verify: compute unit test.
- [ ] 1.4 Translator skips clone/pi on deny. Verify: Translator unit test.

## 2. Wire siblings as they land

- [ ] 2.1 Hook env class from `add-worker-environments` as check (1). Verify: unconfirmed class ⇒ deny.
- [ ] 2.2 Hook isolation from `harden-pi-worker-sandbox` as check (4) when that change exposes a port. Verify: strong request on a host without strong driver ⇒ `admission.sandbox`.
- [ ] 2.3 Hook secret refs from `agent-runtime-capabilities` as check (6). Verify: unresolvable ref ⇒ `admission.secrets`.
- [ ] 2.4 Hook `EditionGate` as check (5) for catalog paid flags. Verify: Community + paid GPU class ⇒ `admission.edition`.

## 3. Gates

- [ ] 3.1 `dotnet build comuki.slnx -c Debug` and architecture tests pass.

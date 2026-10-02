## 1. Isolate the runner

- [ ] 1.1 Add a verify execution path on Translator (or dedicated entry) that runs restore + build/test opcodes without pi; verify unit test with fake processes.
- [ ] 1.2 Admit the slot with the work item's env class via `ISlotAdmissionEvaluator`; verify class mismatch cannot start.
- [ ] 1.3 Stop registering Host `Process.Start` verify against product trees in Production; verify composition test: Host has no such worker, or it is inert.
- [ ] 1.4 Journal `verify.completed` / `verify.failed` with exit code, no Host crash on non-zero. Verify journal tests.

## 2. Gates

- [ ] 2.1 `dotnet build comuki.slnx -c Debug` and architecture tests pass.

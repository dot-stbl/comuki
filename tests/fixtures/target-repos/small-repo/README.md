# small-repo (fixture)

A tiny fixture repo for the agentic-test-contour scenario corpus
(`tests/fixtures/scenarios/small-repo/add-null-check.scenario.yaml`) — not
part of the Comuki platform build (no `.csproj` here, so `dotnet build
comuki.slnx` never touches it).

`src/OrderTotal.cs` has one real bug: `OrderTotal.Calculate` dereferences
its `orders` parameter without a null guard. The `add-null-check` scenario's
ticket asks a worker to add the guard and a regression test.

## Current status (WS6 / T2a)

Per D4 of `openspec/changes/add-agentic-test-contour/design.md`, a scenario's
`ticket.targetRepo` is meant to be cloned into the worker container through
the `SourceGitUrl`/`SourceGitRef` mechanism proposed by
`openspec/changes/harden-pi-worker-sandbox` (tasks 4.1 "Add SourceGitUrl /
SourceGitRef on Project" and 4.3 "Translator clones HTTPS into cwd after
claim and before pi"). **Neither task has landed yet** — verified by
grepping the current tree for `SourceGitUrl` (only a match in
`KubernetesEgressOptions.cs`'s doc comment, "FQDN allowlisting lands with
SourceGitUrl" — the field itself does not exist on `Project`).

T2a (`Comuki.EndToEnd.AgentLoop`) proves container lifecycle — provisioning
→ gRPC → journal — with `TestFakePi` standing in for `pi`, and TestFakePi
never reads this fixture's files at all (it streams a fixed, bundled
pi-native JSON transcript regardless of its working directory). So T2a's
real container run does not mount or clone this repo; this directory exists
so the scenario schema, the fixture corpus shape, and this README are ready
for whichever workstream lands the clone-into-container seam (T2b/WS7) —
that workstream should point its clone at this fixture unchanged.

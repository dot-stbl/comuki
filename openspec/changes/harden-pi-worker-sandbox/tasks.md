## 1. Slice: mint virtual key into pi

- [x] 1.1 Add `MintAsync` to `IVirtualKeyStore` plus an in-memory overlay that sits beside config-seeded keys; verify unit tests: minted token `FindAsync` hits, `RemoveAsync` misses, config-seeded keys still resolve.
- [x] 1.2 On claim 200, mint a key (project, lease expiry, allowed models) and return `proxyBaseUrl` + `virtualKey` on the claim body; verify claim contract tests and that the journal never contains the raw token.
- [x] 1.3 Translator keeps the claim key in cycle memory and `PiRunner` sets `ANTHROPIC_BASE_URL` / `ANTHROPIC_AUTH_TOKEN` on `ProcessStartInfo.Environment` for that process only; verify Translator unit test that the fake pi env contains those two names and not an upstream key.
- [x] 1.4 Revoke the minted key on complete, fail, and lease-lost (including 409 heartbeat); verify proxy rejects the token after each path.
- [x] 1.5 `dotnet build comuki.slnx -c Debug` and the touched unit projects pass.

## 2. Slice: egress fail-closed

- [x] 2.1 Add `Compute:AllowUnfencedEgress` (default false) and a typed fence error; verify options bind + ValidateOnStart.
- [x] 2.2 Kubernetes provider attaches a default-deny NetworkPolicy (DNS + proxy + Nexus + SourceGit host CIDR) before the Job is Ready; if the policy cannot be applied, `StartAsync` fails unless the flag is true; verify mapping tests and a unit test that missing policy + flag false does not create the Job.
- [x] 2.3 Docker provider documents and uses an internal worker network; start fails when the network is absent and the flag is false; verify Docker mapping / skip-if-no-docker tests.
- [x] 2.4 Effective allowlist = profile ∩ project with defaults (proxy, Nexus, SourceGit host); Brain/claim cannot add hosts; verify a pure allowlist intersection test.

## 3. Slice: limits and hardening

- [x] 3.1 Docker `HostConfig` sets Memory + NanoCpus from provider options (profile shape later); verify mapping test.
- [x] 3.2 Kubernetes Job sets CPU/memory **limits** as well as requests; verify mapping test that the pod spec contains both.
- [x] 3.3 Kubernetes `securityContext`: runAsNonRoot, drop ALL, allowPrivilegeEscalation false, RuntimeDefault seccomp; Docker: User, CapDrop ALL, no-new-privileges; verify mapping tests.
- [x] 3.4 Worker image runs as non-root USER; `pi --version` still works as that user; verify Dockerfile + image sanity (existing override-entrypoint scenario). *(static verification only — docker unavailable on the dev machine; one CI `docker build` tracked in #125)*
- [x] 3.5 `dotnet build comuki.slnx -c Debug` passes.

## 4. Follow-up: workspace clone

- [ ] 4.1 Add `SourceGitUrl` / `SourceGitRef` on Project (EF + PATCH + views); verify create/patch tests and that ProfilesGitUrl is unchanged.
- [ ] 4.2 Add optional git-credential secret ref on project settings; verify settings round-trip.
- [ ] 4.3 Translator clones HTTPS into cwd after claim and before pi; missing URL or private-without-credential fails the item; credential not exported to pi env; verify Translator tests with a local git fixture.

## 5. Follow-up: conditions, drain, debug, pin, fixture

- [ ] 5.1 Journal `WorkspacePrepared` / `EgressApplied` / `AgentRunning` on the bound run; verify journal payload tests.
- [ ] 5.2 Translator drain of brief/result/pins before complete/fail; packager skips already-bundled; drain error does not skip complete; verify packager + translator tests.
- [ ] 5.3 Opt-in debug exec (default off) for operators; verify 403/refuse when off.
- [ ] 5.4 Production start requires image digest; tag-only fails in Production; verify provider test.
- [ ] 5.5 `translator --fixture` runs prepare + fake pi without claim; verify a unit/integration test with a fixture file.

## 6. Follow-up: pi-extensions

- [ ] 6.1 Wire default locks through a pi-extension so a test-file edit is denied at tool-call time; verify worker-sdk + an integration with TestFakePi or pi hook test.
- [ ] 6.2 Register materialized skills with pi via the extension; `listSkills` alone does not count; verify a skill is callable after prepare.
- [ ] 6.3 Worker MCP client from `COMUKI_MCP_URL` in the worker-sdk (not dev-sdk); unset URL → no MCP tools; verify unit tests.

## 7. Cross-slice gate

- [ ] 7.1 Cowork `worker-pools` spec notes this change as the floor for `strong` (comment or cross-link only — no slot implementation).
- [x] 7.2 Final `dotnet build comuki.slnx -c Debug` after each merged slice; no AX/Substrate package references in the tree.

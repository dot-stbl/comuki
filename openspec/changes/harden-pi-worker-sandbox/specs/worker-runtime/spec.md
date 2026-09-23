## ADDED Requirements

### Requirement: Virtual key is minted on claim and stamped into pi
On a successful claim the orchestrator SHALL mint a virtual model key bound to that work item, with expiry no later than the lease. The Translator SHALL pass `ANTHROPIC_BASE_URL` (the proxy) and `ANTHROPIC_AUTH_TOKEN` (the minted token) in the environment of **that** pi process, not as a container-lifetime secret. Complete, fail, and lease-lost SHALL revoke the key. Pi SHALL NOT receive a raw upstream provider key.

#### Scenario: Pi talks only to the proxy
- **WHEN** pi is spawned for a claimed item
- **THEN** its environment contains the proxy base URL and a token that the proxy accepts, and does not contain the upstream API key

#### Scenario: Lease lost kills the key
- **WHEN** the lease is lost or the item completes
- **THEN** the minted token is rejected by the proxy

### Requirement: Workspace is prepared before pi starts
After claim and before `pi` is spawned, the Translator SHALL materialize the working directory: clone the project's `SourceGitUrl` at `SourceGitRef` over HTTPS into the working directory, apply the existing profiles overlay, and only then start pi with that directory as cwd. A missing source URL, a failed clone, or a private clone without a configured git credential SHALL fail the work item and SHALL NOT start pi.

#### Scenario: Clone then spawn
- **WHEN** a project has `SourceGitUrl` and the credential (if required) resolves
- **THEN** pi's working directory contains that checkout before the first agent event

#### Scenario: Missing source fails the item
- **WHEN** `SourceGitUrl` is unset
- **THEN** the item is failed with a typed workspace error and pi is not started

### Requirement: Git credential never enters pi env
A project MAY name a secret reference used as an HTTPS git credential. The Translator SHALL use it only for the clone and SHALL NOT export it into the pi process environment.

#### Scenario: Token not visible to the agent
- **WHEN** a private repository is cloned with a project git credential
- **THEN** pi's environment does not contain that credential value

### Requirement: Artifact drain before complete
Before complete or fail, the Translator SHALL attempt to persist the run artifact bundle (brief/result/pins, plus any working-tree evidence the packager already expects). A drain failure SHALL be journaled and SHALL NOT skip complete/fail. The host packager remains the fallback for terminal runs that were not drained.

#### Scenario: Drain then complete
- **WHEN** pi exits successfully
- **THEN** the artifact store has the run prefix objects (or a drain-failed journal entry) before the REST complete call returns

### Requirement: Fixture mode without the orchestrator
The Translator SHALL accept a local fixture work item (file or stdin) that runs prepare + agent spawn without claiming from the host, for tests of workspace and environment stamping.

#### Scenario: Fixture prepare
- **WHEN** the Translator is started with a fixture that names a source git URL
- **THEN** it prepares the working directory and either spawns the configured agent or the test fake, without calling claim

## MODIFIED Requirements

### Requirement: Translator loop
The worker's outer loop SHALL run claim → execute → report → repeat until the process stops; an empty claim waits the poll interval (default 10 seconds) before retrying. One cycle:

1. claim over REST (empty → return)
2. mint-stamp of the virtual model key for this work item (orchestrator mints; Translator receives it with the claim)
3. prepare the working directory: clone `SourceGitUrl` at `SourceGitRef`, then profiles material under `profiles/` (copy from the mounted path when configured, else shallow-clone the public git URL at the pinned ref, else warn and skip). Journal `WorkspacePrepared` / `EgressApplied` accordingly. Failure here fails the item and skips spawn.
4. open the gRPC session, send StageStart
5. spawn the agent executable with the minted proxy URL and token in its environment, working directory = prepared cwd, flags `-p <brief> --mode json --no-session`, and pump its stream-json output, forwarding text deltas / authoritative assistant text / tool invocations as Activity events while a heartbeat task extends the lease every interval (default 30 seconds) and a command task consumes orchestrator commands. Journal `AgentRunning` when the process has started.
6. send the final StageReport, close the session
7. drain artifacts, then: if the lease was lost (rejected heartbeat or a `LeaseExpired` command): skip completion entirely — the reaper owns the item; else complete on `success` (result JSON = the serialized StageReport) or fail with a `status: error-text` reason otherwise. Revoke the minted key in all terminal paths.

Failures propagate and stop the host — an ephemeral worker is meant to die and be replaced, not limp along.

#### Scenario: Non-zero pi exit fails the item
- **WHEN** the spawned agent process exits non-zero
- **THEN** the outcome is `failed` carrying the exit code and stderr, and the item is failed over REST

#### Scenario: Lease lost mid-run
- **WHEN** a heartbeat is rejected (409) while pi still runs
- **THEN** pi is cancelled, the report says `cancelled`, the minted key is revoked, and no complete/fail is written — the reaper owns the item

#### Scenario: Result text is authoritative
- **WHEN** the agent streams text deltas and later a message-end assistant text
- **THEN** the summary replaces accumulated deltas with the authoritative final wording

#### Scenario: Prepare failure skips spawn
- **WHEN** the source repository cannot be cloned
- **THEN** pi is not started and the item is failed

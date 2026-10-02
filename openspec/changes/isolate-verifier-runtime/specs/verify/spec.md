## Purpose

Defines product verification as an admitted compute slot using the work item's environment class, never as `Process.Start` inside the API host.

## ADDED Requirements

### Requirement: Verify runs in an admitted slot
Verification of a work item (restore already done, then build/test/format as the project declares) SHALL run in a compute slot whose SlotAdmission carries the same `env_class` as the work item. The API host process SHALL NOT start the product toolchain (`dotnet`, `bun`, compilers) to verify that item.

#### Scenario: Host does not run dotnet build for a work item
- **WHEN** verify is requested for an item bound to `net10-sdk-bun`
- **THEN** `dotnet build` of that solution does not appear as a child process of `Comuki.Host`

#### Scenario: Verify uses the item's class
- **WHEN** the item's env class is `net10-sdk`
- **THEN** the verifier slot is admitted as `net10-sdk`, not `net10-sdk-bun` and not the runtime-only image

### Requirement: Verify may skip the coding agent
A verify execution SHALL run restore (if not already warm) plus class-advertised verify opcodes (for example `dotnet build`, `dotnet run` of test projects, `bun` test scripts). It SHALL NOT be required to spawn pi. Failure of an opcode fails verify with the command's exit and a truncated log, not a Host crash.

#### Scenario: Build failure is a verify report
- **WHEN** `dotnet build comuki.slnx -c Debug` exits non-zero in the verifier slot
- **THEN** the run journal records `verify.failed` with exit code and the Host remains up

### Requirement: No docker.sock and no Host cwd
The verifier SHALL NOT be given the Host docker socket. The working directory SHALL be the slot workspace (cloned target), not the Host's current directory.

#### Scenario: Socket is absent
- **WHEN** a verifier slot starts
- **THEN** `/var/run/docker.sock` is not mounted

#### Scenario: Cwd is the slot workspace
- **WHEN** verify runs `dotnet build comuki.slnx`
- **THEN** the process working directory is the cloned target in the slot, not the Host process cwd

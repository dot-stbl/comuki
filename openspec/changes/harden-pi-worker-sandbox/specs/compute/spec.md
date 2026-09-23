## ADDED Requirements

### Requirement: Provider applies the egress fence
`StartAsync` SHALL apply the effective egress allowlist before the worker process can send traffic. Docker SHALL place the container on an isolated network whose only routes are the allowlisted destinations. Kubernetes SHALL attach a network policy (or equivalent CNI fence) that default-denies egress except those destinations. If the fence cannot be applied, `StartAsync` SHALL fail unless `Compute:AllowUnfencedEgress` is true.

#### Scenario: Docker start is fenced
- **WHEN** the Docker provider starts a worker
- **THEN** the container cannot open a TCP connection to a host outside the effective allowlist

#### Scenario: Missing fence aborts start
- **WHEN** the provider cannot attach a fence and the unfenced flag is false
- **THEN** no container or Job is created and the caller receives a typed fence error

### Requirement: Resource limits on every start
A start SHALL set CPU and memory **limits** (not only Kubernetes requests). Limits come from the profile resource shape, falling back to provider options. Docker SHALL set memory and nano-CPU limits. Kubernetes SHALL set both requests and limits on the Job pod.

#### Scenario: Docker memory cap
- **WHEN** a worker starts under Docker with a 1Gi memory limit
- **THEN** the container is killed by the runtime if it exceeds 1Gi

#### Scenario: Kubernetes limits present
- **WHEN** a worker Job is created
- **THEN** the pod spec has CPU and memory limits, not requests alone

### Requirement: Hardening defaults
Every worker container or Job SHALL run as a non-root user, drop all capabilities, set `allowPrivilegeEscalation` false (or Docker equivalent no-new-privileges), and apply the runtime default seccomp profile. The root filesystem MAY remain read-write. The worker SHALL NOT be given a Docker socket.

#### Scenario: Non-root worker
- **WHEN** a worker starts
- **THEN** the agent process uid is not 0

#### Scenario: No docker socket
- **WHEN** a worker starts
- **THEN** `/var/run/docker.sock` is not mounted into the container

### Requirement: Production image pin is a digest
A production start SHALL use an image reference that includes a digest. An untagged or tag-only image SHALL be rejected in production. Development MAY continue to pin `{image}:{hostVersion}`.

#### Scenario: Tag-only refused in production
- **WHEN** the host is in Production and the configured worker image has no digest
- **THEN** `StartAsync` fails and no worker is created

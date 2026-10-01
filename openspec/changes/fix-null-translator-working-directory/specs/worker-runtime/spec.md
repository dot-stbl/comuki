## MODIFIED Requirements

### Requirement: Translator environment contract

The worker container's `COMUKI_*` environment SHALL map onto the Translator configuration: `COMUKI_ORCH_HTTP` (REST base URL), `COMUKI_ORCH_GRPC` (gRPC URL), `COMUKI_WORKER_TOKEN`, `COMUKI_PROFILE_KEY`, `COMUKI_PROFILES_REF`, `COMUKI_WORKER_IMAGE`, `COMUKI_PROFILES_PATH`, `COMUKI_PROFILES_GIT_URL`, `COMUKI_PI_EXECUTABLE`, `COMUKI_WORKING_DIRECTORY`. Options validate on start — a missing orchestrator URL or token fails the boot.

The in-memory config snapshot that bridges the container env onto the `Translator` section SHALL OMIT every entry whose `COMUKI_*` source is unset or empty rather than write a `null` (or empty-string) value: an unset entry MUST NOT overwrite the option's default. Required options (orchestrator URLs, worker token, profile key, profiles ref, worker image) keep their `Required` validation — a missing required value still fails the boot.

#### Scenario: Container env becomes config

- **WHEN** the compute provider stamps `COMUKI_WORKER_TOKEN` and `COMUKI_ORCH_GRPC` on the container
- **THEN** the Translator authenticates and connects without further configuration

#### Scenario: Unset `COMUKI_WORKING_DIRECTORY` keeps the default working directory

- **WHEN** the container is started without `COMUKI_WORKING_DIRECTORY` set (the worker image's documented default; nothing in the per-claim env contract stamps it)
- **THEN** `TranslatorOptions.WorkingDirectory` keeps its `Directory.GetCurrentDirectory()` default and `ProfilesProvider.PrepareAsync` does not throw `ArgumentNullException` on `Path.Combine(opts.WorkingDirectory, "profiles")`

#### Scenario: Unset `COMUKI_PROFILES_*` keeps the documented skip-with-warning behavior

- **WHEN** the container is started without `COMUKI_PROFILES_PATH` and without `COMUKI_PROFILES_GIT_URL`
- **THEN** `ProfilesProvider.PrepareAsync` logs a warning and skips the profiles prepare step rather than throwing on a `null` option

#### Scenario: An unset required option still fails the boot

- **WHEN** the container is started without `COMUKI_WORKER_TOKEN` (a required option with no default)
- **THEN** `ValidateOnStart` throws naming the missing option and the host does not enter the claim loop
## Purpose

Defines the public versioned HTTP description and generated client-contract pipeline shared by dashboard, CLI, and other operator clients.

## ADDED Requirements

### Requirement: URL-segment API versioning
The public operator API SHALL use URL-segment versions managed through the platform API versioning system. Resources use lowercase nested noun segments rather than hyphenated compound resources, for example `/api/v1/work/tasks`, `/api/v1/mission/rooms`, `/api/v1/capability/operations`, and `/api/v1/worker/hosts`. Product UI routes are independent of this grammar.

#### Scenario: Breaking API generation
- **WHEN** a released contract requires a breaking change
- **THEN** the platform may expose a new version group while the previous group carries deprecation and sunset metadata during its migration window

### Requirement: Canonical OpenAPI documents
Built-in ASP.NET OpenAPI generation SHALL produce one canonical document per API version with XML descriptions, stable operation ids, tags, ProblemDetails, security schemes, typed examples, and `x-comuki-*` capability metadata supplied by transformers. Operator HTTP operations are included; internal/runtime/bootstrap capability catalogs are excluded.

#### Scenario: Operator reads API schema
- **WHEN** the v1 document is requested
- **THEN** it describes every public v1 operator route and omits worker protocol and internal-only capability definitions

### Requirement: Branded Scalar reference
The Host SHALL serve a Comuki-branded Scalar reference at `/api/description` with version selection. Canonical raw documents SHALL remain machine-readable under `/api/description/{version}/openapi.json`. Self-hosted deployments expose the reference publicly by default and MAY require authentication or disable it through typed configuration.

#### Scenario: Public reference disabled
- **WHEN** deployment policy disables public API description
- **THEN** anonymous access is denied while authorized generation and CI artifacts remain available

### Requirement: Shared generated TypeScript contract package
A root Bun workspace SHALL contain a committed generated package at `platform/generated/ts`, published internally as the Comuki client contracts package. One root `bun run codegen` SHALL emit OpenAPI/schema documents, run Kubb once, generate REST types/client plus Mission stream/capability schemas, format output, and support a CI drift check. Dashboard and future CLI rewrites SHALL consume this package rather than maintain independent DTO mirrors.

#### Scenario: Contract drift
- **WHEN** C# HTTP or realtime contracts change without committed regeneration
- **THEN** the codegen drift check fails and both dashboard/CLI consumer builds reference the same expected package version

### Requirement: Forward-compatible stream decoding
Realtime discriminated unions SHALL be present in OpenAPI components or a versioned JSON Schema document consumed by the same codegen pipeline. Generated decoders SHALL preserve envelope identity/sequence and safely represent unknown future payload kinds without disconnecting clients.

#### Scenario: Older client sees new event kind
- **WHEN** a server sends a Mission event payload introduced after the client was built
- **THEN** the client keeps cursor ordering, renders an unknown-event fallback, and continues processing later events

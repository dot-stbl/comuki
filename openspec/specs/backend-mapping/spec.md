# backend-mapping Specification

## Purpose
Defines how backend modules project domain entities onto API view records:
source-generated mappers per module, init-property view records, and the
boundary where hand-written mapping remains correct — with wire shapes
guaranteed stable across any mapping migration.

## Requirements

### Requirement: Entity-to-view projections are source-generated

Every module adopted into this convention (the adoption list starts with
Projects; each follow-up wave appends its module) SHALL project domain
entities onto API view records through a source-generated mapper: one
singular `I{Module}Mapper` interface per module's application layer, one
`[Mapper]`-attributed partial implementation registered as a singleton, with
strict target mapping — an unmapped view property SHALL fail the build, not
silently default. Handlers SHALL depend on the interface, never on the
implementation class.

#### Scenario: Missing target property fails the build

- **WHEN** a developer adds a property to an adopted module's view record
  without a source for it
- **THEN** `dotnet build comuki.slnx -c Debug` fails with a Mapperly
  diagnostic naming the unmapped target property

#### Scenario: New view projection joins the mapper

- **WHEN** an adopted module gains a new entity→view projection
- **THEN** it is added as a method on the module's existing mapper interface,
  and no new static mapper class appears in the module's application layer

### Requirement: View records are init-property records

View records of adopted modules SHALL expose init-only properties with no
positional constructor, so mappers object-initialize them and an unpinned
consumer cannot construct a half-filled view. This is a code-shape rule, not
a wire rule: the serialized property set SHALL remain identical to the
pre-adoption shape (same names, same casing, same nullability, same JSON).

#### Scenario: View gains a property with no mapping source

- **WHEN** a view property is added without a corresponding source
- **THEN** compilation fails (strict target mapping), rather than the view
  serializing a default value at runtime

#### Scenario: Wire bytes unchanged by a mapping migration

- **WHEN** an adopted module's views migrate from positional to init-property
  records and from hand-written to generated mapping
- **THEN** the module's REST responses serialize the same property names and
  values as before the migration, and the dashboard's generated API client
  regenerates with zero diff

### Requirement: Hand-written mapping stays at the format boundary

Mapping SHALL remain hand-written exactly where a source generator cannot
carry the logic: parsing external wire payloads into domain types (provider
webhook/payload mappers), serialization-format round-trips (cache entries),
and request-DTO→command shaping in the host. Field-for-field entity→view
shuffling SHALL NOT be hand-written in adopted modules. Mapping domain
entities onto views stays entity→view only; mappers never take a view or
DTO as source.

#### Scenario: Payload mapper remains hand-written

- **WHEN** a provider webhook body must be parsed into a domain ticket
- **THEN** that parsing lives in a hand-written static mapper in the
  provider's infrastructure layer, outside the generated-mapper convention

#### Scenario: Contract-to-view projection is not force-migrated

- **WHEN** a view is projected from a cross-module contract read model
  rather than a domain entity (e.g. usage-event summaries)
- **THEN** it stays hand-written until its module owns a domain entity for
  the data — the convention covers entity→view projections

### Requirement: Migration waves are module-scoped and wire-pinned

Adoption SHALL proceed per module, each wave independently shippable, each
pinned by the wire-stability guarantee above (serialization pin tests plus a
zero-diff generated-client gate). The wave order and per-module sizing live
in the adopting change's design document; Scheduler adoption waits for the
in-flight scheduled-jobs change to land first.

#### Scenario: A wave ships alone

- **WHEN** one module's mapping migration merges
- **THEN** no other module's mapper, views, or handlers are touched, and
  that module's REST surface is byte-identical before and after

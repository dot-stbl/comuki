## ADDED Requirements

### Requirement: Host composition has no product-toolchain runner for work items
Host composition SHALL NOT register a hosted worker that `Process.Start`s product toolchains (`dotnet`, `bun`, `cmake`, …) against a work item or project source tree. Host `doctor` / self health MAY invoke short host binaries unrelated to product gates.

#### Scenario: Community host still composes
- **WHEN** the host boots with Verify isolated to compute slots
- **THEN** `ValidateOnBuild` succeeds and no verify worker in the API process starts `dotnet build`

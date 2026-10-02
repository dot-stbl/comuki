## ADDED Requirements

### Requirement: Scalar EnvClass until Repository lands
A Project SHALL carry `EnvClass` (catalog id or empty) as the stand-in binding for its scalar source repository. Create and PATCH SHALL accept it with null-means-untouched semantics. Empty `EnvClass` SHALL make implement work items for that source unclaimable. When `add-multi-repo-projects` migrates `SourceGitUrl` to a primary `ProjectRepositoryAttachment`, `EnvClass` SHALL move onto that Repository row and the Project field SHALL cease to be source of truth. `ProfilesGitUrl` remains unrelated.

#### Scenario: Create with class
- **WHEN** a project is created with `envClass = "net10-sdk-bun"`
- **THEN** subsequent reads return that class and implement items copy it at enqueue

#### Scenario: Empty class blocks implement
- **WHEN** a project has `SourceGitUrl` set and `EnvClass` empty
- **THEN** implement work items for that source are not claimable

#### Scenario: Migration moves the field
- **WHEN** the primary-attachment migration runs for a project with `EnvClass = "net10-sdk"`
- **THEN** the registered Repository carries `EnvClass = "net10-sdk"` and enqueue reads the Repository, not the Project scalar

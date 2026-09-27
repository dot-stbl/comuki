## ADDED Requirements

### Requirement: Project repository attachments
A Project SHALL attach zero or more Repositories (from the `repositories`
capability) through a `ProjectRepositoryAttachment` carrying a role
(`primary` | `service` | `frontend` | `library` | `deploy-gitops` | `docs` |
...) and an access level (`write` | `read` | `external`) (R1). The same
Repository MAY be attached to more than one Project; attachments are
independent per Project — mutating one Project's attachment (role, access,
credential override) SHALL NOT affect another Project's attachment to the
same Repository.

#### Scenario: Attaching a repository already attached elsewhere
- **WHEN** a Repository already attached to Project A with role `primary` is
  attached to Project B with role `library`
- **THEN** Project A keeps its `primary` attachment unchanged and Project B's
  `library` attachment is independent

#### Scenario: Detaching does not delete the Repository
- **WHEN** a Project removes its attachment to a Repository still attached
  to another Project
- **THEN** the Repository row and the other Project's attachment are
  unaffected

### Requirement: Primary attachment migration from scalar source fields
When `feature/source-workspace-clone`'s scalar `Project.SourceGitUrl` /
`Project.SourceGitRef` fields are present, the platform SHALL migrate each
non-null `SourceGitUrl` into a registered `Repository` and an auto-created
`ProjectRepositoryAttachment(role: primary, access: write)` carrying
`SourceGitRef` as the attachment's pinned ref. Once the primary attachment
exists for a Project, worker workspace resolution SHALL prefer it over the
scalar fields; the scalar fields remain readable during the migration window
but SHALL NOT be the source of truth once a primary attachment exists.
`Project.ProfilesGitUrl` / `Project.ProfilesGitRef` (the worker-profiles
overlay) are unaffected by this migration — they are not product-repository
fields and stay scalar.

#### Scenario: Existing scalar source migrates to a primary attachment
- **WHEN** a Project with `SourceGitUrl` set has the migration applied
- **THEN** a Repository is registered for that URL and the Project gains a
  `primary`/`write` attachment carrying the prior `SourceGitRef` as its
  pinned ref

#### Scenario: Profiles fields are untouched by migration
- **WHEN** a Project with both `ProfilesGitUrl` and `SourceGitUrl` set has
  the migration applied
- **THEN** `ProfilesGitUrl`/`ProfilesGitRef` remain scalar fields on the
  Project, unaffected by the new attachment

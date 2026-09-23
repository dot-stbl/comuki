## ADDED Requirements

### Requirement: Source repository on the project
A project SHALL carry `SourceGitUrl` and `SourceGitRef` (branch, tag, or digest) naming the product repository workers check out. These are distinct from `ProfilesGitUrl` / `ProfilesGitRef`. PATCH SHALL include them with the same null-means-untouched semantics as profiles fields. The slug remains immutable. The host of `SourceGitUrl` is the git destination in the project's default egress allowlist.

#### Scenario: Create with source
- **WHEN** a project is created with `sourceGitUrl` and `sourceGitRef`
- **THEN** subsequent reads return those values and workers clone that URL

#### Scenario: Profiles url is not the product repo
- **WHEN** only `ProfilesGitUrl` is set
- **THEN** workspace prepare still requires `SourceGitUrl` and fails without it

### Requirement: Optional git credential secret ref
Project settings SHALL accept an optional secret reference used as an HTTPS credential for `SourceGitUrl`. Absence is valid for public repositories. For a private URL, a missing or unresolvable ref SHALL cause workspace prepare to fail.

#### Scenario: Public clone without a credential
- **WHEN** `SourceGitUrl` is a public HTTPS URL and no git credential is configured
- **THEN** prepare clones without a token

#### Scenario: Private clone without a credential
- **WHEN** `SourceGitUrl` is private and the git credential ref is unset
- **THEN** prepare fails and pi is not started

## MODIFIED Requirements

### Requirement: Slug is immutable
The slug is the stable external key other modules reference. Update SHALL be partial (PATCH semantics — null fields leave stored values untouched) over name, description, profiles git URL, profiles git ref, source git URL and source git ref; the slug SHALL NOT be editable through any endpoint.

#### Scenario: Rename without slug change
- **WHEN** a caller patches only the name
- **THEN** the name changes and every other field, including the slug, stays

#### Scenario: Patch source git
- **WHEN** a caller patches `sourceGitUrl` and `sourceGitRef`
- **THEN** those fields change and the slug and profiles git fields stay

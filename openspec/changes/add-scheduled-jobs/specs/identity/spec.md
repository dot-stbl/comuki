## MODIFIED Requirements

### Requirement: Compiled permission catalog and role matrix

The permission vocabulary SHALL be declared in one place as `dot:verb` keys (e.g. `run:read`, `plan:approve`, `chat:use`, `settings:write`, `identity:write`, `platform:admin`, `schedule:read`, `schedule:write`). The role→permissions map SHALL be compiled in code, not data: changing what a role can do is a commit and a release. Every declared key MUST be held by at least one role — a unit test holds that invariant.

#### Scenario: Roles are fixed
- **WHEN** a deployment wants a custom role
- **THEN** the answer is a code change, not a database row

## ADDED Requirements

### Requirement: Schedule permissions

The permission vocabulary SHALL include `schedule:read` and `schedule:write`. Role assignment:

- `schedule:read` — viewer, member, approver, project-admin, operator, platform-admin
- `schedule:write` — project-admin, operator, platform-admin

Member SHALL NOT receive `schedule:write` (cron + secret env refs are operator surface, unlike `source:write` on member). Startup validation of `RequiresPermission` demands continues to fail boot on an undeclared key.

#### Scenario: Viewer lists schedules

- **WHEN** a viewer-role subject GETs `/api/v1/projects/{id}/schedules`
- **THEN** the list is returned (object axis still 404s out-of-scope projects)

#### Scenario: Member cannot create a schedule

- **WHEN** a member-role subject POSTs a new schedule
- **THEN** the answer is 403 `permission.denied`

#### Scenario: Project-admin can write

- **WHEN** a project-admin POSTs a schedule on their project
- **THEN** the job is created (201)

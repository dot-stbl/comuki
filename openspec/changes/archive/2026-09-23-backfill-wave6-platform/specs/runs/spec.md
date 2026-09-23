## ADDED Requirements

### Requirement: Runs list API
`GET /api/v1/runs` SHALL list runs for the current subject with permission
`run:read`. Results SHALL be paged and filterable/sortable through the shared
filter DSL (see filtering). Filterable fields SHALL include `Status`
(eq/in/notIn) and `CreatedAt` / `UpdatedAt` (range, `now()`). Subject-scope
query filters SHALL hide out-of-scope rows (absent, not 403). Parse failures
answer 400 with code `filter.invalid`.

#### Scenario: Filter by status
- **WHEN** a caller requests `?filter=status==Running`
- **THEN** only runs in `Running` (visible in subject scope) are returned

#### Scenario: Out-of-scope run omitted
- **WHEN** a run exists in a project outside the subject's scope
- **THEN** it does not appear in the list response

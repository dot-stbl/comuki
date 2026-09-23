## Purpose

Defines the shared list filter/sort DSL used by host list endpoints (runs
today; other aggregates later): grammar, operators, pagination envelope, and
error shape.

## ADDED Requirements

### Requirement: FilterQuery envelope
List endpoints that support the DSL SHALL accept query parameters `filter`,
`sort`, `page` (1-based, default 1), and `pageSize` (default 25, clamped to
[1, 100]). Null/whitespace filter or sort means "none".

#### Scenario: Page size clamp
- **WHEN** a client requests pageSize 500
- **THEN** the handler clamps to 100

### Requirement: Filter grammar
The filter language SHALL support AND (`;`) binding tighter than OR (`|`),
parentheses, comparisons (`== != ~ ^= $= >= <= > < []= ? !?`), quoted/bare
values, and `now(signedDuration)` on date/time fields with units
`s|m|h|d|w`. Null operators `?` / `!?` take no value and apply only to
nullable/reference fields. Unknown fields, illegal operators, bad conversions,
or malformed functions SHALL raise a parse error mapped to HTTP 400 with code
`filter.invalid`.

#### Scenario: Relative time window
- **WHEN** filter is `createdAt>=now(-7d)`
- **THEN** only rows with CreatedAt in the last seven days (server UTC) match

#### Scenario: Invalid expression
- **WHEN** filter references an unknown field or bad operator for a field
- **THEN** the API answers 400 `filter.invalid`

### Requirement: Sort grammar
Sort SHALL be `field` or `field,direction` criteria separated by `;`, with
direction `asc|desc` (case-insensitive). Multiple criteria apply as
OrderBy/ThenBy. Unknown sort fields are skipped per criterion (stale-client
safe).

#### Scenario: Multi-sort
- **WHEN** sort is `updatedAt,desc;status,asc`
- **THEN** primary order is UpdatedAt descending with Status as tie-break

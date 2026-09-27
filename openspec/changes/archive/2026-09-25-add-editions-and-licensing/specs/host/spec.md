## ADDED Requirements

### Requirement: `GET /api/v1/edition` route is host-owned

The host SHALL expose `GET /api/v1/edition` as an anonymous endpoint (no `[RequiresPermission]`, no `[RequiresFeature]`, no `[Authorize]`) so the dashboard's first paint can render an upsell affordance before the user signs in. The route template SHALL be exposed as a constant in `ApiRoutes` (`Edition = "/api/v1/edition"`) and SHALL be used by the controller's `[Route]` attribute rather than a literal route string. The response payload shape is owned by the `editions` capability (`specs/editions/spec.md`'s "GET /api/v1/edition endpoint" requirement); this requirement owns only the route's presence and its host-side wiring. The host's OpenAPI emission SHALL include the route as an anonymous operation, and a Debug `dotnet build comuki.slnx` SHALL emit `artifacts/openapi.json` containing `/api/v1/edition` with the documented response shape.

#### Scenario: Anonymous reader gets the Community view

- **WHEN** an unauthenticated client calls `GET /api/v1/edition` under a Community (absent) license
- **THEN** the response is 200 with the edition view describing the Community tier, the available feature set, the limit caps, and `version` sourced from `ComukiBuildInformation`

#### Scenario: Authenticated reader under a paid tier gets the paid view

- **WHEN** an authenticated client calls `GET /api/v1/edition` under a paid license with `expiry` in the future
- **THEN** the response is 200 with the paid tier code, `status: valid`, the covered feature set, and `expiresAt` carrying the ISO-8601 UTC expiry

### Requirement: `edition.feature_unavailable` and `edition.limit_exceeded` map to 403 problem+json over two paths

The edition-gating axis has TWO independent decision sites, both of which must produce the same 403 `application/problem+json` wire shape with stable dot.case codes. The two paths are:

1. **Request-time filter path** — `RequiresFeatureFilter` (MVC resource filter) and `RequiresFeatureMiddleware` (minimal-API middleware) read `[RequiresFeature]` / `[EnforceLimit]` off the endpoint metadata, consult the live `IEdition` + `IEditionCapabilityRegistry` through `EditionGate`, and short-circuit the request with a 403 problem+json body. This mirrors the Identity module's `RequiresPermissionFilter` precedent (which already writes 401/403 problem+json directly without going through `ProviderExceptionHandler`) — the resource stage is the right place for an inline deny because it wraps model binding and result execution so the decision cannot be bypassed by an earlier short-circuit. The body SHALL carry the same extensions the RBAC arm carries (`code`, plus gate-specific extras for feature denials: `feature` key + `minimumTier`; for limit denials: `limit` key + `cap` + `current`).

2. **Authoritative transactional handler path** — the source of truth for count-quota enforcement under concurrent writers. `CreateProjectHandler` opens a database transaction, takes `pg_advisory_xact_lock(hashtext('limit:projects'))`, counts current non-archived projects inside the same transaction via a count-only `IProjectStore` port (no full list materialisation), compares against the current edition's `Limits.Projects` cap, and either inserts (committing) or refuses. The refusal path throws `ProviderForbiddenException("edition.limit_exceeded", "limit 'projects' is exhausted (current/cap)")` which the central `ProviderExceptionHandler` (`platform/src/host/Comuki.Host/Errors/ProviderExceptionHandler.cs:15`) maps to 403 problem+json via the existing `ProviderForbiddenException` arm (`ExceptionMapping.Map`). No new typed exception is introduced — the same `ProviderForbiddenException` arm serves the OIDC / Disabled / ban / edition-deny cases alike. The `code` extension carries the dot.case code; the `detail` field carries the limit key + current count + cap.

The two paths share the same wire shape: 403, `application/problem+json`, stable dot.case `code` (`edition.feature_unavailable` or `edition.limit_exceeded`), and RFC 9457 problem+json body. The filter path additionally carries UI-facing extensions (`feature`, `minimumTier`, `limit`, `cap`, `current`) in the body — the handler path carries them in `detail` only, because the central exception handler only stamps the `code` extension.

The request-time filter path is **advisory** for count-quota gates: it fast-fails a caller that's already over the cap without burning a DB transaction. The handler path is the source of truth — a race that sneaks past the filter (two concurrent writers both reading `current < cap` simultaneously) loses by a 403 from the handler, never by a successful insert that breaks the cap.

The `ProviderExceptionHandler` body SHALL continue to be built with `TypedResults.Problem(...)` (no hand-rolled JSON) and SHALL preserve the existing 401 / 403 / 404 / 500 / 502 / 504 status-code map.

#### Scenario: Gate-deny on the request-time filter surfaces edition.feature_unavailable

- **WHEN** a `[RequiresFeature(Features.X)]`-gated endpoint is reached and the current edition does not cover the feature
- **THEN** the host answers 403 `application/problem+json` with `code = edition.feature_unavailable`, `detail` naming the feature key and the minimum tier, and extensions carrying the feature key + minimum tier sufficient for the dashboard to render an upsell without a second call

#### Scenario: Limit-exceeded on the request-time filter surfaces edition.limit_exceeded

- **WHEN** an `[EnforceLimit(Limits.Projects)]` gate's fast-fail sees a caller already at or above the cap
- **THEN** the host answers 403 `application/problem+json` with `code = edition.limit_exceeded`, `detail` naming the limit key and the current count, and extensions carrying the limit key + cap + current

#### Scenario: Limit-exceeded on the authoritative handler surfaces edition.limit_exceeded via ProviderExceptionHandler

- **WHEN** a `[EnforceLimit(Limits.Projects)]` gate's filter passed (caller below cap at request time) but two concurrent writers race into `CreateProjectHandler` at cap=1
- **THEN** the second writer's `pg_advisory_xact_lock` blocks until the first commits, the second's count sees the first's insert, the handler throws `ProviderForbiddenException("edition.limit_exceeded", "limit 'projects' is exhausted (1/1)")`, and the host answers 403 `application/problem+json` with `code = edition.limit_exceeded` and `detail` naming the limit key and the current count

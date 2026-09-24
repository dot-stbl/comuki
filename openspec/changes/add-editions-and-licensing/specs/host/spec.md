## ADDED Requirements

### Requirement: `GET /api/v1/edition` route is host-owned

The host SHALL expose `GET /api/v1/edition` as an anonymous endpoint (no `[RequiresPermission]`, no `[RequiresFeature]`, no `[Authorize]`) so the dashboard's first paint can render an upsell affordance before the user signs in. The route template SHALL be exposed as a constant in `ApiRoutes` (`Edition = "/api/v1/edition"`) and SHALL be used by the controller's `[Route]` attribute rather than a literal route string. The response payload shape is owned by the `editions` capability (`specs/editions/spec.md`'s "GET /api/v1/edition endpoint" requirement); this requirement owns only the route's presence and its host-side wiring. The host's OpenAPI emission SHALL include the route as an anonymous operation, and a Debug `dotnet build comuki.slnx` SHALL emit `artifacts/openapi.json` containing `/api/v1/edition` with the documented response shape.

#### Scenario: Anonymous reader gets the Community view

- **WHEN** an unauthenticated client calls `GET /api/v1/edition` under a Community (absent) license
- **THEN** the response is 200 with the edition view describing the Community tier, the available feature set, the limit caps, and `version` sourced from `ComukiBuildInformation`

#### Scenario: Authenticated reader under a paid tier gets the paid view

- **WHEN** an authenticated client calls `GET /api/v1/edition` under a paid license with `expiry` in the future
- **THEN** the response is 200 with the paid tier code, `status: valid`, the covered feature set, and `expiresAt` carrying the ISO-8601 UTC expiry

### Requirement: `edition.feature_unavailable` and `edition.limit_exceeded` are mapped in the host exception handler

The host's `ProviderExceptionHandler` (`platform/src/host/Comuki.Host/Errors/ProviderExceptionHandler.cs:15`) is the single composition-root `IExceptionHandler`. The exception mapping table in `ExceptionMapping.Map` (`ProviderExceptionHandler.cs:72`) SHALL produce a 403 ProblemDetails body for any thrown `ProviderForbiddenException` whose `Code` is `edition.feature_unavailable` or `edition.limit_exceeded`, with the same body shape as the existing `permission.denied` arm (RFC 9457 problem+json, `code` extension carrying the dot.case code, `detail` naming the feature key and the minimum edition required, never leaking license internals such as the embedded public key fingerprint, the signature, or the full payload). The new mapping arms SHALL reuse the existing `ProviderForbiddenException` arm — no new typed exception is introduced — and SHALL preserve the existing 401 / 403 / 404 / 500 / 502 / 504 status-code map. The handler SHALL NOT hand-roll the body; the response SHALL be built with `TypedResults.Problem(...)`.

#### Scenario: Gate-deny surfaces edition.feature_unavailable

- **WHEN** a `[RequiresFeature(Features.X)]`-gated endpoint throws `ProviderForbiddenException("edition.feature_unavailable", "feature 'multi-repo' requires paid tier 'team'")` because the license does not cover the feature
- **THEN** the host answers 403 `application/problem+json` with `code = edition.feature_unavailable`, `detail` naming the feature key and the minimum tier, and a UI-facing extension carrying the feature key + minimum tier sufficient for the dashboard to render an upsell without a second call

#### Scenario: Limit-exceeded surfaces a sibling code on the same arm

- **WHEN** a `[EnforceLimit(Limits.Projects)]` gate throws `ProviderForbiddenException("edition.limit_exceeded", "limit 'projects' exceeded: 1 / 1")`
- **THEN** the host answers 403 `application/problem+json` with `code = edition.limit_exceeded`, `detail` naming the limit key and the current count, and the extension carries the limit key + the cap

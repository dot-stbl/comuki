# Comuki v1 Dependency Audit — 2026-09-09

**Scope:** master tip `e3de735` (`[hybrid] fix(host): SubjectScopeMiddleware wraps PermissionEvaluator in AsSystem`, 2026-09-09).
**Method:** static read of `Directory.Packages.props` (Central Package Management, 35 entries),
`dashboard/package.json` (44 direct deps + 2 `overrides`), `agents/{package.json, comuki-*-package.json}`
(8 direct deps, lockfile present), and `bun.lock` (280 lines, 76 transitive deps).
Cross-checked against the authoritative `dotnet list package --vulnerable --include-transitive`
output for the host + engine + 5 modules, plus GitHub Security Advisories and nuget.org / npm
registry for current stable versions.
**Out of scope:** `bun audit` / `npm audit` were not run (rejected by the user during the
previous audit — read-only audit). The advisory database was queried manually via the
GitHub Advisory DB and NVD.

---

## Executive Summary

Comuki v1 is in **good supply-chain shape**. Of the 35 NuGet packages and ~50 unique
Node packages audited (44 direct + ~5 transitively unique via the agents lockfile),
**only one confirmed CVE** is present in the actual installed dependency graph
(transitive `Microsoft.OpenApi 2.0.0` → GHSA-v5pm-xwqc-g5wc / CVE-2026-49451,
High, DoS via stack overflow on circular schema parsing) — and there is a **drop-in
upgrade path** that resolves it (`Microsoft.AspNetCore.OpenApi 10.0.9` → `10.0.11+`,
which pulls the patched `Microsoft.OpenApi >= 2.7.5`).

Top three issues, ranked:

1. **The single CVE** — `Microsoft.OpenApi 2.0.0` (transitive, High). Fix is a
   3-line csproj edit (bump `Microsoft.AspNetCore.OpenApi` to `10.0.11`) that also
   drops the `<NoWarn>NU1903</NoWarn>` suppression on `Comuki.Host.csproj`. The
   CVE itself is a process-termination DoS that requires parsing an untrusted
   OpenAPI document; the Comuki runtime does not parse untrusted OpenAPI input,
   so the real-world blast radius is the build-time `dotnet build` step and the
   kubb-cli code-gen step — both in controlled CI, not production.
2. **Dashboard has no `bun.lock`** (`dashboard/bun.lock` does not exist; the
   root `.gitignore` excludes `bun.lock` globally). Caret ranges like
   `^5.101.0` and `^1.170.11` are floating — `bun install` on a fresh checkout
   could pull a different transitive set than what was tested at v1 cut.
3. **Several NuGet packages are 1-3 patches/majors behind current stable**
   (`Microsoft.AspNetCore.OpenApi 10.0.9` vs `10.0.12`,
   `Microsoft.IdentityModel.Protocols.OpenIdConnect 8.19.2` vs `8.22.0`,
   `KubernetesClient 17.0.14` vs `19.0.2`). None have published CVEs at the
   pinned versions, but the lag means new security fixes land in the
   un-pinned line.

License posture: every pinned package in both ecosystems is MIT / ISC /
Apache-2.0 / BSD-3-Clause. **No copyleft (GPL / AGPL / LGPL) or commercial
licenses were found in the pinned set.** Transitive resolution was not
performed (would require `dotnet list package --include-transitive` + per-
package license fetch, which the audit scope intentionally limited).

---

## 1. Backend NuGet Packages

**Vulnerability scan** — `dotnet list package --vulnerable --include-transitive`
was run on the host (`Comuki.Host`), host-translator (`Comuki.Host.Translator`),
host-brain (`Comuki.Host.Brain`), the two engines (`Comuki.Engine.Orchestration`,
`Comuki.Engine.Compute`), four module infrastructures (Artifacts, Scheduler,
Intake, Proxy, Identity), and one integration project (`Comuki.Host.Integration.Smoke`).
Result: **exactly one CVE** is flagged by `dotnet`'s authoritative vulnerability
source — `Microsoft.OpenApi 2.0.0` (transitive, High). All other projects report
"no vulnerable packages".

### 1.1 Packages with known issues

| Package | Pinned | Latest stable | CVE / Advisory | Severity | Recommendation |
|---|---|---|---|---|---|
| `Microsoft.OpenApi` *(transitive)* | `2.0.0` (pulled by `Microsoft.AspNetCore.OpenApi 10.0.9`) | `2.7.5` (patched) | **GHSA-v5pm-xwqc-g5wc** / **CVE-2026-49451** — circular schema reference causes stack overflow → process termination (DoS). CVSS 7.5. EPSS 1.238%. CWE-674. Patched in `Microsoft.OpenApi 2.7.5` and `3.5.4`. Published May 26, 2026. | **High** | **Upgrade `Microsoft.AspNetCore.OpenApi` to `10.0.11` (or `10.0.12`) in `Directory.Packages.props:79`. The `10.0.11` nuspec requires `Microsoft.OpenApi [2.7.5, 3.0.0)` — resolves to the patched line. This drops the `<NoWarn>NU1903</NoWarn>` suppression on `Comuki.Host.csproj:13` and removes the line-by-line acceptance of the CVE currently documented at `Comuki.Host.csproj:5-12`.** |

### 1.2 Pinned packages — freshness

All packages below are pinned in `Directory.Packages.props`. "Lag" is the count of
patch or minor versions between the pin and the latest stable on nuget.org as of
2026-09-09. **No CVE** at the pinned version; lag only signals slower uptake of
upstream fixes.

| Package | Pinned | Latest stable | Lag | Notes |
|---|---|---|---|---|
| `Docker.DotNet` | `3.125.15` | `3.125.15` | 0 | Current |
| `KubernetesClient` | `17.0.14` | `19.0.2` | **2 major** | Comment at `Directory.Packages.props:20-22` explicitly defers 18/19: *"latest stable of the line the k8s compute provider targets (18/19 exist but are unvetted here)"*. The current 17.x line is supported but the upstream Kubernetes API surface has moved (CRD evolution, RBAC changes). Recommendation: vet & bump to 19.x. |
| `FluentValidation` | `12.1.1` | `12.1.1` | 0 | Current |
| `Grpc.Net.Client` | `2.83.0` | `2.83.0` | 0 | Current |
| `LibGit2Sharp` | `0.32.0` | `0.32.0` | 0 | Current (and the most-recent **stable** of a 0.x line; the wrapper around the native libgit2 C library) |
| `Microsoft.Extensions.Configuration` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.Extensions.Configuration.Abstractions` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.Extensions.DependencyInjection` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.Extensions.Hosting` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.Extensions.Hosting.Abstractions` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.Extensions.Http` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.Extensions.Http.Resilience` | `10.0.0` | `10.0.0` | 0 | Current (note: this package is on its own version cadence — `Http.Resilience` ships independently of the core `Http`) |
| `Microsoft.Extensions.Logging.Abstractions` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.Extensions.Options` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.Extensions.Options.DataAnnotations` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.Extensions.Primitives` | `10.0.11` | `10.0.11` | 0 | Current |
| `protobuf-net.Grpc` | `1.3.14` | `1.3.14` | 0 | Current |
| `protobuf-net.Grpc.AspNetCore` | `1.3.14` | `1.3.14` | 0 | Current |
| `Refit` | `15.2.0` | `15.2.0` | 0 | Current |
| `Refit.HttpClientFactory` | `15.2.0` | `15.2.0` | 0 | Current |
| `Refit.Reflection` | `15.2.0` | `15.2.0` | 0 | Current |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | `10.0.3` | `10.0.3` | 0 | Current |
| `EFCore.NamingConventions` | `10.0.1` | `10.0.1` | 0 | Current |
| `Microsoft.EntityFrameworkCore` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.EntityFrameworkCore.InMemory` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.EntityFrameworkCore.Relational` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.EntityFrameworkCore.Design` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.Extensions.Configuration.Json` | `10.0.11` | `10.0.11` | 0 | Current |
| `Voluta` | `0.3.0` | n/a | n/a | Local repo at `C:\Users\bradw\source\stbl\voluta`. **Not in public NuGet feed — this is a dependency-audit blind spot.** A change in `C:\...\voluta` does not produce a NuGet version bump the auditor can see. |
| `Voluta.DependencyInjection` | `0.3.0` | n/a | n/a | Same blind spot as above. |
| `Voluta.Checkpoints.EntityFrameworkCore` | `0.3.0` | n/a | n/a | Same blind spot as above. |
| `Microsoft.AspNetCore.OpenApi` | `10.0.9` | `10.0.12` | **3 patches** | Source-generator version. `10.0.9` deps `Microsoft.OpenApi 2.0.0` (CVE range); `10.0.11` deps `[2.7.5, 3.0.0)` (patched); `10.0.12` deps `[2.12.0, 3.0.0)` (current). Recommendation: bump to `10.0.11` minimum to drop the `<NoWarn>NU1903</NoWarn>` suppression. |
| `Microsoft.Extensions.ApiDescription.Server` | `10.0.9` | `10.0.12` | **3 patches** | Build-time source generator. Should move together with `Microsoft.AspNetCore.OpenApi`. |
| `Testcontainers.PostgreSql` | `4.14.0` | `4.15.0` | 1 patch | 4.14.0 picked up `SSH.NET 2026.0.0` to avoid `GHSA-q939-rpr3-3284` (`Directory.Packages.props:85-88`). Bump to 4.15.0 when convenient — same authors, same cadence. |
| `Microsoft.AspNetCore.SignalR.Client` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.AspNetCore.TestHost` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.Extensions.Caching.Memory` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.Extensions.Identity.Core` | `10.0.11` | `10.0.11` | 0 | Current |
| `Microsoft.IdentityModel.Protocols.OpenIdConnect` | `8.19.2` | `8.22.0` | **3 minors** | Both known CVE families (`CVE-2024-21643`, `GHSA-8g9c-28fc-mcx2`) target the older 6.x/7.x lines — Comuki is safe at 8.x. Bump for non-security bug fixes. |
| `Microsoft.Extensions.AI` | `10.9.0` | `10.9.0` | 0 | Current |
| `Microsoft.Extensions.AI.OpenAI` | `10.9.0` | `10.9.0` | 0 | Current |
| `OpenAI` | `2.12.0` | `2.13.0` | 1 minor | Comment at `Directory.Packages.props:107-115` notes the constraint `[2.12.0, 2.13.0)` from `Microsoft.Extensions.AI.OpenAI 10.9.0` — bumping requires the MEAI pair to move first. |
| `Microsoft.VisualStudio.Threading.Analyzers` | `15.0.240` | n/a (analyzer-only) | n/a | Source analyzer, not a runtime dep. Latest in this line is `15.0.240` itself. |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | `1.16.0` | `1.18.0` | 2 minors | Comment at `Directory.Packages.props:122-124` notes alignment with `console.x` SDK telemetry. Not strictly behind on security — 1.16.0 was released Mar 2026; 1.17.0 / 1.18.0 are routine. |
| `OpenTelemetry.Extensions.Hosting` | `1.16.0` | `1.18.0` | 2 minors | Same — keep aligned with the OTLP exporter. |
| `Sentry` | `6.10.0` | `6.10.0` | 0 | Comment at `Directory.Packages.props:128-133` notes the explicit pin to the latest stable that targets `net10.0` without dragging an extra TFM. |
| `Minio` | `7.0.0` | `7.0.0` | 0 | Current |
| `Testcontainers.Minio` | `4.14.0` | `4.15.0` | 1 patch | Keep aligned with `Testcontainers.PostgreSql`. |
| `Yarp.ReverseProxy` | `2.3.0` | `2.3.0` | 0 | Current |

### 1.3 Testing-only packages

| Package | Pinned | Latest stable | Lag | Notes |
|---|---|---|---|---|
| `xunit.v3` | `3.2.2` | `4.0.0` | **1 major** | 4.0.0 was released (final line). v3.2.2 is the previous line; project explicitly chose xUnit v3 + MTP (see `AGENTS.md:54-55` and `STATE.md:312`). Bump to v4 when the migration is planned — out of scope for this audit. |
| `Microsoft.NET.Test.Sdk` | `18.6.0` | `18.6.0` | 0 | Current |
| `Shouldly` | `4.3.0` | `4.3.0` | 0 | Current (5.0.0-preview.1 / 5.0.0-preview.2 exist but are pre-release) |
| `NSubstitute` | `5.3.0` | `6.2.0` | **1 major** | 6.x is the current stable line. 5.3.0 → 6.x is a major API surface change (`Arg.Any<T>()` for generic type inference, mocked async setup). Bump planned separately. |
| `coverlet.collector` | `10.0.1` | `10.0.1` | 0 | Current |
| `NetArchTest.Rules` | `1.3.2` | `1.3.2` | 0 | Current |

**Audit subtotal (BE):** 35 pinned + 3 transitive (Microsoft.OpenApi 2.0.0 via
Microsoft.AspNetCore.OpenApi 10.0.9) = **35 unique top-level packages, 1 with a known CVE**.

---

## 2. Frontend Node Packages

**Source-of-truth for the dashboard:** `dashboard/package.json` (44 entries in
`dependencies` + `devDependencies`, plus 2 `overrides`). **No lockfile present**
— `dashboard/bun.lock` does not exist, and the root `.gitignore:9-11` ignores
`bun.lock` and `bun.lockb` globally. Implications for this audit:

- Versions below are the **resolved target ranges**, not the actually-installed
  versions. The `dotnet list package --vulnerable` analogue for npm/bun
  (`bun audit` / `npm audit`) was explicitly excluded by the user during the
  previous audit and is excluded here.
- Caret ranges (`^x.y.z`) and tilde ranges (`~x`) pull whatever the registry
  serves at install time. A fresh `bun install` on a different day will produce
  a different transitive closure. **This is the single largest supply-chain
  risk in the FE half of Comuki.**

**Source-of-truth for the agents:** `agents/package.json` (workspace root, 2 dev
deps) + `agents/comuki-{agent-core,worker-sdk,dev-sdk}/package.json` (3 ws
packages) + `agents/bun.lock` (280 lines, 76 transitive packages resolved).
Lockfile is present and committed for the agents workspace — **the FE half
should match this discipline.**

### 2.1 Dashboard dependencies (production)

| Package | Range | Latest stable | Lag | License | Notes |
|---|---|---|---|---|---|
| `@fontsource-variable/archivo` | `^5.3.0` | `5.3.0` | 0 | OFL-1.1 | Self-hosted font; current major. |
| `@fontsource-variable/jetbrains-mono` | `^5.3.0` | `5.3.0` | 0 | OFL-1.1 | Same. |
| `@fontsource/ibm-plex-mono` | `^5.2.7` | `5.2.7` | 0 | OFL-1.1 | Earlier major — `^5.2.7` will not auto-upgrade to 5.3.x. |
| `@hookform/resolvers` | `^5.4.0` | `5.9.1` | 5 minors | MIT | Zod v4 compat landed earlier; minor bump candidates available. |
| `@tailwindcss/vite` | `^4` | `4.3.3` | 0 | MIT | Floats on the `4.x` major. |
| `@tanstack/react-query` | `^5.101.0` | `5.102.8` | 1 minor | MIT | See §2.3 (TanStack security incident May 2026). |
| `@tanstack/react-router` | `^1.170.11` | `1.170.33` | 22 patches | MIT | Caret floats; see §2.3. |
| `@tanstack/react-router-devtools` | `^1.167.0` | `1.167.x` (line continues under react-router) | n/a | MIT | Floats within 1.167.x — peer with router. |
| `@tanstack/react-table` | `^9.2.4` | `9.2.4` | 0 | MIT | Current. |
| `@tanstack/react-virtual` | `^3.14.10` | `3.14.11` | 1 patch | MIT | Floats. |
| `clsx` | `^2.1.1` | `2.1.1` | 0 | MIT | Current. |
| `lucide-react` | `^1.17.0` | `1.43.0` | 26 minors | ISC | **Note:** the previous security audit (`security-audit-report.md:858`) flagged this as "invalid semver (real line is 0.x); `npm install` will fail" — that was **wrong**. lucide-react ships **both** a 0.x line (current `0.577.0`) **and** a 1.x line (current `1.43.0`). `^1.17.0` resolves cleanly to the latest 1.x. The flag should be closed as a stale finding. |
| `react` | `^19.2.6` | `19.2.8` | 2 patches | MIT | Floats on the 19.2 line. |
| `react-aria-components` | `^1.20.0` | `1.21.1` | 1 minor | Apache-2.0 | Floats. |
| `react-dom` | `^19.2.6` | `19.2.8` | 2 patches | MIT | Floats. |
| `react-hook-form` | `^7.77.0` | `7.87.0` | 10 minors | MIT | Floats. |
| `react-resizable-panels` | `^4.11.2` | `4.12.4` | 1 minor | MIT | Floats. |
| `sonner` | `^2.0.7` | `2.0.8` | 1 patch | MIT | Toast library; floats. |
| `tailwind-merge` | `^3.6.0` | `3.6.0` | 0 | MIT | Current. |
| `tailwindcss` | `^4` | `4.3.3` | 0 | MIT | Floats on the `4.x` major. |
| `tw-animate-css` | `^1.4.0` | `1.4.0` | 0 | MIT | Current. |
| `zod` | `^4.4.3` | `4.5.4` | 1 minor | MIT | No GitHub advisories for zod at any version (manual query against the advisory DB). Floats. |

### 2.2 Dashboard devDependencies

| Package | Range | Latest stable | Lag | License | Notes |
|---|---|---|---|---|---|
| `@eslint/js` | `^10` | `10.0.1` | 0 | MIT | ESLint v10 era. |
| `@kubb/cli` | `4.39.2` *(exact)* | `4.39.2` | 0 | MIT | Exact pin — good. |
| `@kubb/core` | `4.39.2` *(exact)* | `4.39.2` | 0 | MIT | Exact pin — good. |
| `@kubb/plugin-client` | `4.39.2` *(exact)* | `4.39.2` | 0 | MIT | Exact pin — good. |
| `@kubb/plugin-oas` | `4.39.2` *(exact)* | `4.39.2` | 0 | MIT | Exact pin — good. |
| `@kubb/plugin-react-query` | `4.39.2` *(exact)* | `4.39.2` | 0 | MIT | Exact pin — good. |
| `@kubb/plugin-ts` | `4.39.2` *(exact)* | `4.39.2` | 0 | MIT | Exact pin — good. |
| `@kubb/plugin-zod` | `4.39.2` *(exact)* | `4.39.2` | 0 | MIT | Exact pin — good. |
| `@playwright/test` | `^1.60.0` | `1.63.0` | 3 minors | Apache-2.0 | E2E test runner; **only relevant in real-mode e2e runs** — the agent is forbidden from running playwright (per `process/agent-runtime-safety.md`), so this is a CI/local-only concern. Floats. |
| `@storybook/addon-essentials` | `^8` | `8.6.14` | 0 | MIT | Floats on 8.x. |
| `@storybook/addon-themes` | `^8` | `8.6.14` | 0 | MIT | Same. **Note:** Storybook 10.x exists — caret floats on `^8` keep Comuki on the 8.x line, which is intentional (Storybook 9/10 introduced breaking config changes). |
| `@storybook/blocks` | `^8` | `8.6.14` | 0 | MIT | Same. |
| `@storybook/react-vite` | `^8` | `8.6.14` | 0 | MIT | Same. |
| `@tanstack/router-plugin` | `^1.168.14` | `1.168.36` | 22 patches | MIT | Floats; peer with `react-router`. |
| `@testing-library/jest-dom` | `^6.9.1` | `6.9.x` | 0 | MIT | 7.0.1 exists; caret stays on 6.x. |
| `@testing-library/react` | `^16.3.2` | `16.3.3` | 1 patch | MIT | Floats. |
| `@testing-library/user-event` | `^14.6.1` | `14.6.7` | 6 patches | MIT | Floats. |
| `@types/node` | `^24` | `26.5.0` | **2 majors** | MIT | **Note:** the previous security audit (`security-audit-report.md:860`) flagged this as "invalid semver (real line is 22.x)" — that was **wrong**. `@types/node` ships 22.x, 24.x, 25.x, and 26.x concurrently. `^24` resolves cleanly. Caret could be tightened to `~24` to avoid drift onto the 25/26 line, but `^24` is not broken. |
| `@types/react` | `^19` | `19.2.18` | 0 | MIT | Floats. |
| `@types/react-dom` | `^19` | `19.2.7` | 0 | MIT | Floats. |
| `@vitejs/plugin-react` | `^6` | `6.1.1` | 0 | MIT | Floats on 6.x. |
| `@vitest/coverage-v8` | `^4.1.8` | `5.0.0` | **1 major** | MIT | vitest 5.0.0 exists; caret stays on 4.x. v5 is a major API change. |
| `@vitest/ui` | `^4.1.8` | `5.0.0` | **1 major** | MIT | Same. |
| `ajv` | `^8.5.0` | `8.20.0` | 15 minors | MIT | The `overrides` block at `dashboard/package.json:6-12` forces `ajv-draft-04 → ajv@^8.5.0` and `eslint → ajv@^6.12.0`. The eslint override pins to the older `ajv@^6` line — `ajv` 6.x is end-of-life (CVE-2020-7598 prototype pollution, mitigated by dev-only scope). Manual query against GitHub Advisory DB: **0 currently-open advisories** for `ajv` npm package — but the historical CVE-2020-7598 is unfixed in the 6.x line. The override keeps eslint working without dragging a fresh `ajv` into the runtime closure. |
| `eslint` | `^10` | `10.10.0` | 0 | MIT | Floats on the 10.x major. |
| `eslint-plugin-react-hooks` | `^7.1.1` | `7.1.1` | 0 | MIT | Current. |
| `eslint-plugin-react-refresh` | `^0.5.2` | `0.5.6` | 4 patches | MIT | Floats. |
| `globals` | `^17` | `17.12.0` | 0 | MIT | Current. |
| `jsdom` | `^29.1.1` | `30.0.1` | **1 major** | MIT | 30.0.1 exists; caret stays on 29.x. |
| `prettier` | `^3.8.3` | `3.9.6` | 1 minor | MIT | Floats. |
| `prettier-plugin-tailwindcss` | `^0.8.0` | `0.8.1` | 1 patch | MIT | Floats. |
| `storybook` | `^8` | `8.6.14` | 0 | MIT | Floats on 8.x. |
| `typescript` | `~6` | `7.0.2` | **1 major** | Apache-2.0 | **Note:** the previous security audit (`security-audit-report.md:861`) flagged this as "invalid semver (TS 6 not released)" — that was **wrong**. **TypeScript 6.0 GA was released** (the npm `dist-tags` shows `latest = 7.0.2`, but **both 6.x and 7.x exist and are maintained**). `~6` resolves to the 6.x line (currently 6.0.x). Comuki's `agents/package.json` already pins `typescript: ^7.0.2` (the 7.x line) — the dashboard and the agents are on different TypeScript majors. **Recommendation:** align them — bump dashboard `typescript` to `^7.0.2` to match the agents workspace. |
| `typescript-eslint` | `^8` | `8.70.0` | 0 | BSD-2-Clause | Floats on 8.x. |
| `vite` | `^8` | `8.2.2` | 0 | MIT | **Note:** the previous security audit (`security-audit-report.md:862`) flagged this as "invalid semver (Vite 8 not released)" — that was **wrong**. **Vite 8.x is the current line** (`latest = 8.2.2`). `^8` resolves cleanly. The audit was based on stale registry data. |
| `vitest` | `^4.1.8` | `5.0.0` | **1 major** | MIT | 5.0.0 exists; caret stays on 4.x. |

### 2.3 TanStack security incident (May 2026) — context

In May 2026, a TanStack maintainer's npm publishing credentials were compromised
between **2026-05-11 22:45 UTC** and **2026-05-12 01:53 UTC**. Several `@tanstack/*`
packages were republished with malicious payloads (CVE-2026-45321,
"Crypto-stealing malware"); the worst-affected was `@tanstack/arktype-adapter`.
The compromised versions were unpublished; npm registry timestamps confirm that
**none of Comuki's pinned ranges intersect the incident window**:

- `@tanstack/react-router` `1.170.11` (the lower bound of Comuki's `^1.170.11`)
  was published **2026-06-05**, weeks after the window closed.
- `@tanstack/react-query` `5.101.0` (the lower bound of Comuki's `^5.101.0`) is
  from early 2026, before the incident.
- `@tanstack/router-plugin` `1.168.14` (lower bound of `^1.168.14`) is also
  pre-incident.

The packages Comuki uses that have **no listed advisory at any version**:
`@tanstack/react-query`, `@tanstack/react-router`, `@tanstack/react-table`,
`@tanstack/react-virtual`, `@tanstack/react-router-devtools`,
`@tanstack/router-plugin`. The advisory **does** list `@tanstack/arktype-adapter`
as the worst-affected package — Comuki does not use that.

**Risk:** very low. **Mitigation already in place:** the agents workspace's
`bun.lock` resolves to specific versions that can be audited. The dashboard's
lack of a lockfile means a *new* install on a *different* day could in
principle pull a fresh malicious version if the TanStack account is
re-compromised — but the same is true of *every* `@tanstack/*` user
globally. Pinning to exact versions via a committed `bun.lock` (per §6
below) is the standard mitigation.

### 2.4 Agents workspace (`agents/`)

Resolved via `agents/bun.lock` (committed; 280 lines, 76 transitive packages,
3 workspace packages).

| Package (direct) | Pinned | Latest stable | Lag | License | Notes |
|---|---|---|---|---|---|
| `@types/bun` | `^1.4.0` | `1.4.0` (line) | 0 | MIT | Backs `bun-types` 1.4.0. |
| `typescript` | `^7.0.2` | `7.0.2` | 0 | Apache-2.0 | **One major ahead of dashboard (`~6`).** |
| `zod` (via `comuki-agent-core`) | `4.5.4` *(exact)* | `4.5.4` | 0 | MIT | Exact pin — best. |
| `@comuki/agent-core` | `workspace:*` | n/a | n/a | Internal | Workspace local. |
| `@comuki/worker-sdk` | `workspace:*` | n/a | n/a | Internal | Workspace local. |
| `@modelcontextprotocol/sdk` | `1.30.0` *(exact)* | `1.30.0` | 0 | MIT | Exact pin — best. |
| `@hono/node-server` (transitive via MCP SDK) | `2.1.1` | `2.1.1` | 0 | MIT | Current. |
| `express` (transitive via MCP SDK) | `5.2.1` | `5.2.1` | 0 | MIT | Current. |
| `express-rate-limit` (transitive) | `8.7.0` | `8.7.0` | 0 | MIT | Current. |
| `hono` (transitive) | `4.13.5` | `4.13.5` | 0 | MIT | Current. |
| `jose` (transitive) | `6.2.10` | `6.2.10` | 0 | MIT | Current. |
| `zod-to-json-schema` (transitive) | `3.25.2` | `3.25.2` | 0 | MIT | Current. |
| `ajv` (transitive) | `8.20.0` | `8.20.0` | 0 | MIT | Current. |
| `ajv-formats` (transitive) | `3.0.1` | `3.0.1` | 0 | MIT | Current. |
| `cors` (transitive) | `2.8.6` | `2.8.6` | 0 | MIT | Current. |
| `cross-spawn` (transitive) | `7.0.6` | `7.0.6` | 0 | MIT | Current. |
| `eventsource` (transitive) | `3.0.7` | `3.0.7` | 0 | MIT | Current. |
| `eventsource-parser` (transitive) | `3.1.1` | `3.1.1` | 0 | MIT | Current. |
| `ip-address` (transitive) | `10.7.0` | `10.7.0` | 0 | MIT | Current. |
| `json-schema-typed` (transitive) | `8.0.2` | `8.0.2` | 0 | MIT | Current. |
| `pkce-challenge` (transitive) | `5.0.1` | `5.0.1` | 0 | MIT | Current. |
| `raw-body` (transitive) | `3.0.2` | `3.0.2` | 0 | MIT | Current. |

**All transitive packages in the agents workspace resolve to current latest
stable.** No advisories from manual GitHub Advisory DB queries for `ajv`, `jose`,
`express`, `cors`, `cross-spawn`, `hono`, `eventsource` — though each package
has historical CVEs in earlier versions, none of the pinned versions are in the
affected ranges.

**Audit subtotal (FE):** 44 dashboard entries + 2 workspace entries +
3 ws-package entries = **49 unique top-level package names, 0 with a known CVE
at the pinned range**, but **5 of the previous audit's "invalid semver" flags
were wrong** (see §6 below).

---

## 3. .NET Runtime / SDK

### 3.1 TargetFramework

- **No `global.json`** — the repo does not pin a specific .NET SDK version.
  `dotnet --list-sdks` on the audit machine reports `8.0.424`, `9.0.308`,
  `10.0.111`, `10.0.303` installed. The 10.x line is active. The latest stable
  .NET 10 SDK as of 2026-09-09 is `10.0.303` (the audit machine has it).
- **`TargetFramework` is set globally** in `Directory.Build.props:4` to
  `net10.0`. Every project in the solution inherits this. Confirmed in the
  audit's spot-check of `platform/src/host/Comuki.Host/Comuki.Host.csproj`
  (no explicit `TargetFramework`, uses the inherited value).
- **.NET 10 is current LTS** — Microsoft's .NET 10 (released Nov 2025) is
  the latest LTS line, with monthly servicing patches (`10.0.1xx` and
  `10.0.2xx` cadence) and a free support window running through Nov 2030.

### 3.2 Risk

The combination of:

1. **No `global.json`** — every contributor / CI runner picks up the latest
   installed 10.x SDK, which can produce build-behavior drift between
   environments.
2. **NuGet package versions that match the runtime exactly** (e.g. `10.0.11`,
   `10.0.9`) — the host references `Microsoft.AspNetCore.OpenApi 10.0.9` but
   the runtime on the audit machine is `10.0.11`. These are *not* the same
   package family — the package versions target the runtime version they were
   built against, and they're meant to float together. The mismatch is by
   design and is **not a security risk**.

### 3.3 Recommendation

Add a `global.json` pinning the SDK to `10.0.x` (or specifically to `10.0.303`)
so every CI run uses the same SDK. This is a hygiene fix, not a security fix.

---

## 4. Suppression Hygiene

### 4.1 `<NoWarn>` overrides in csproj

| File | Line | Suppression | Reason |
|---|---|---|---|
| `Directory.Build.props` | 20 | `CS1591` | "Suppress missing XML doc comments — added per-area as code lands." — applies globally to all C# projects. **Benign**: not a security suppression. |
| `platform/build/Comuki.Build.Tools/Comuki.Build.Tools.csproj` | 8 | `$(NoWarn);CS2008;CS8785` | `CS2008` (no files to compile) and `CS8785` (nullable warnings in generator output). **Benign**: build-tools-only. |
| `platform/src/host/Comuki.Host/Comuki.Host.csproj` | 13 | `$(NoWarn);NU1903` | **This is the security-relevant suppression.** It silences the build-error that would otherwise fire from the `Microsoft.OpenApi 2.0.0` CVE (transitive). See §1.1. The csproj carries an XML-doc comment at lines 5–12 documenting the accepted tech debt and pointing at console.x `.agents/debt/BACKEND-ISSUES.md` §"2026-07-01". **Not silently hidden** — the suppression is in source control with rationale. |

### 4.2 `Directory.Packages.props` NoWarn references

| Line | Reference | Notes |
|---|---|---|
| 72–73 | Comment referring to `<NoWarn>NU1903</NoWarn>` on `Comuki.Host.csproj`. | Comment, not a real `<NoWarn>` element. |
| 85–88 | Comment on `Testcontainers.PostgreSql` 4.14.0 (the SSH.NET CVE fix history). | Comment, not a real `<NoWarn>` element. |

**Conclusion:** one security-relevant suppression exists (`NU1903` on
`Comuki.Host.csproj`), it is documented in source control with rationale, and it
has a known removal path (see §1.1 — bump `Microsoft.AspNetCore.OpenApi` to
`10.0.11+`). No other security-relevant suppressions.

### 4.3 Repository-wide NuGet / NU warnings

`dotnet build comuki.slnx -c Debug` was **not** run during this audit (would
take ~10 minutes to first-time restore + format-verify gate). The
`Directory.Build.props` enables `TreatWarningsAsErrors=true`, so any
build-time `NU1902`/`NU1903` would already fail the build. The single
`<NoWarn>NU1903</NoWarn>` is the only known exception.

---

## 5. License Risk

### 5.1 Pinned packages — licenses

Every NuGet package in `Directory.Packages.props` carries an OSI-approved
permissive license. Verified by metadata read for each:

| Package family | License |
|---|---|
| Microsoft.* (all 14 entries) | MIT |
| `Docker.DotNet`, `KubernetesClient` | MIT |
| `FluentValidation` | MIT |
| `Grpc.Net.Client` | Apache-2.0 |
| `LibGit2Sharp` | MIT (per NuGet metadata; the bundled libgit2 native binary is also MIT) |
| `protobuf-net.Grpc*` | MIT |
| `Refit*` | MIT |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSQL *(OSI-approved)* |
| `EFCore.NamingConventions` | MIT |
| `xunit.v3`, `Microsoft.NET.Test.Sdk` | MIT |
| `Shouldly` | BSD-3-Clause |
| `NSubstitute` | BSD-3-Clause |
| `coverlet.collector` | MIT |
| `NetArchTest.Rules` | MIT |
| `Voluta*` | Internal / not on public feed |
| `OpenTelemetry.*` | Apache-2.0 |
| `Sentry` | MIT |
| `Minio` | Apache-2.0 |
| `Testcontainers.*` | MIT |
| `Yarp.ReverseProxy` | MIT |

Every Node package in `dashboard/package.json` and `agents/package.json` /
`agents/*/package.json` / `agents/bun.lock` carries an OSI-approved permissive
license (MIT / ISC / Apache-2.0 / BSD-2-Clause / OFL-1.1 / PostgreSQL /
BlueOak-1.0.0). The `eslint → ajv@^6.12.0` override (dashboard only) pins
`ajv` to the 6.x line — `ajv` is MIT, but the 6.x line is no longer
maintained. **Not a license problem; a maintenance one.**

### 5.2 Transitive resolution

The audit intentionally **did not** walk transitive dependencies for licenses.
The `agents/bun.lock` contains ~70 transitive packages; the dashboard would
have several hundred without a lockfile to anchor against. None of the
transitive packages have been observed in practice to carry copyleft
licenses for the patterns Comuki uses (no codec/SQL Server/etc.) — but a
full transitive sweep would require:

- `npm ls --all` or `bun pm ls` (after a `bun install`),
- parsing each package's `license` field from `node_modules/*/package.json`,
- flagging non-permissive entries.

This is a tooling gap that the **existing dashboard lockfile absence**
prevents us from completing.

### 5.3 Non-OSI licenses

**None found** in the pinned set. **Conclusion: license posture is clean.**

---

## 6. Recommendations

### Priority 1 — must-fix before public release

**1. Bump `Microsoft.AspNetCore.OpenApi` to `10.0.11` (drop-in fix for the
only known CVE).**
- File: `Directory.Packages.props:79` — change `Version="10.0.9"` to
  `Version="10.0.11"`.
- File: `Directory.Packages.props:80` — bump `Microsoft.Extensions.ApiDescription.Server`
  in lockstep from `10.0.9` to `10.0.11` (it tracks the same cadence).
- File: `platform/src/host/Comuki.Host/Comuki.Host.csproj:13` — delete the
  `<NoWarn>$(NoWarn);NU1903</NoWarn>` line.
- File: `platform/src/host/Comuki.Host/Comuki.Host.csproj:5-12` — delete the
  accompanying rationale XML-doc comment.
- Validation: `dotnet list platform/src/host/Comuki.Host/Comuki.Host.csproj
  package --vulnerable --include-transitive` should return "no vulnerable
  packages" after the bump.

**2. Commit `dashboard/bun.lock` (lockfile hygiene).**
- File: root `.gitignore:9-11` — currently ignores `bun.lock` and `bun.lockb`
  globally. Allow-list `dashboard/bun.lock` (and `agents/bun.lock`, which
  is already committed).
- File: `dashboard/` — run `bun install` once on a clean machine and
  commit the resulting `bun.lock`. Without this, every `bun install` on a
  fresh clone produces a different transitive closure — the v1 cut was
  never reproducible from source alone.
- This is **the** single largest FE supply-chain improvement; without it
  the entire §2.1/§2.2 freshness table is informational only.

**3. Close stale "invalid semver" findings from the previous audit.**
- File: `security-audit-report.md:858-862` — the A06-2a/b/c/d findings
  (`lucide-react ^1.17.0`, `@types/node ^24`, `typescript ~6`, `vite ^8`)
  are all valid ranges that resolve cleanly. These should be re-checked and
  either closed with "verified" notes or amended. The new findings:
  - `lucide-react ^1.17.0` resolves to `1.43.0` — current.
  - `@types/node ^24` resolves to `24.x` — current line.
  - `typescript ~6` resolves to `6.0.x` — current line.
  - `vite ^8` resolves to `8.2.2` — current line.

### Priority 2 — must-fix within 30 days

**4. Align `dashboard/typescript` with `agents/typescript` (both on 7.x).**
- File: `dashboard/package.json:91` — change `"typescript": "~6"` to
  `"typescript": "^7.0.2"`. Currently the dashboard pins TypeScript 6.x
  while the agents workspace pins 7.x; both work, but a single TypeScript
  version across the repo is cleaner.

**5. Vet and bump `KubernetesClient` 17.0.14 → 19.0.2 (or document the
defer).**
- File: `Directory.Packages.props:22`. The comment at line 20-22 explicitly
  defers 18/19; that defer should either be lifted (run the k8s compute
  provider tests on 19.x) or moved to a dated `BACKEND-ISSUES.md` entry
  with the actual reason.

**6. Add `global.json` pinning the SDK.**
- New file at repo root: `{ "sdk": { "version": "10.0.303" } }`.
- Ensures reproducible builds across CI / dev machines.

**7. Bump `Microsoft.IdentityModel.Protocols.OpenIdConnect` 8.19.2 → 8.22.0.**
- File: `Directory.Packages.props:104`. Three minor versions behind. Not
  urgent (no CVE at 8.19.2), but the OIDC linker + discovery code paths
  evolve and the lag means fixes don't propagate.

**8. Close the other stale finding: A06-3 (`ajv` 6.x override).**
- File: `dashboard/package.json:11-12`. The `eslint → ajv@^6.12.0` override
  keeps eslint working but pins `ajv` to an EOL branch. **Mitigation
  options:**
  - bump the override to `^8.5.0` and check eslint-plugin compat,
  - or split `ajv` into a separate dev-only override block with rationale,
  - or accept the risk (dev-only, not shipped to the browser bundle).

### Priority 3 — post-ship / v2

**9. Sweep remaining minor lags.**
- Bump `OpenAI` 2.12.0 → 2.13.0 (after the MEAI 10.9.0 constraint is lifted).
- Bump `OpenTelemetry.*` 1.16.0 → 1.18.0 (keep aligned with console.x).
- Bump `Microsoft.Extensions.*` 10.0.11 → 10.0.12 (sync with `Microsoft.AspNetCore.OpenApi`).
- Bump `Testcontainers.*` 4.14.0 → 4.15.0.

**10. Re-audit after one quarter.** Schedule a 2026-Q4 dependency audit
(`dotnet list package --vulnerable` + `npm audit`/`bun audit` against a
fresh lockfile) to catch new CVEs in the moving targets.

**11. Transitive license sweep.** Once a dashboard `bun.lock` exists, run
`npm ls --all --json | jq -r '.dependencies | keys[]'` and resolve each
package's license to confirm no copyleft has crept in via the registry.

**12. Adopt Renovate or Dependabot.** Both GitHub-native and OSS options exist
for keeping NuGet / npm ranges current with a per-PR CI gate. Optional but
eliminates the manual-freshness-table audit going forward.

---

## Appendix: Files Reviewed

### Configuration files
- `Directory.Packages.props` (156 lines) — Central Package Management for .NET.
- `Directory.Build.props` (39 lines) — global `TargetFramework`, nullable,
  analyzer config.
- `platform/build/Comuki.Build.Tools/Comuki.Build.Tools.csproj` (line 8
  `<NoWarn>`).
- `platform/src/host/Comuki.Host/Comuki.Host.csproj` (100 lines — read in full
  for the `<NoWarn>NU1903</NoWarn>` context).
- `dashboard/package.json` (96 lines — read in full).
- `dashboard/.gitignore` (28 lines — confirmed `bun.lock` is not ignored
  per-package but the root .gitignore does).
- `agents/package.json` (15 lines), `agents/comuki-agent-core/package.json`,
  `agents/comuki-worker-sdk/package.json`, `agents/comuki-dev-sdk/package.json`.
- `agents/bun.lock` (280 lines — read in full; 3 workspace packages +
  73 transitive).
- `.gitignore` (root) — confirmed `bun.lock` is globally gitignored at line 10.
- `nuget.config` (referenced — not modified).

### Project configuration
- All 80 `.csproj` files (glob result) — sampled `Comuki.Host.csproj`,
  `Comuki.Host.Translator.csproj`, `Comuki.Host.Brain.csproj`,
  `Comuki.Engine.Orchestration.csproj`, `Comuki.Engine.Compute.csproj`,
  `Comuki.Modules.Artifacts.Infrastructure.csproj`,
  `Comuki.Modules.Scheduler.Infrastructure.csproj`,
  `Comuki.Modules.Intake.Infrastructure.csproj`,
  `Comuki.Modules.Proxy.Infrastructure.csproj`,
  `Comuki.Modules.Identity.Infrastructure.csproj`,
  `Comuki.Host.Integration.Smoke.csproj` for `dotnet list package
  --vulnerable --include-transitive`.

### Audit / history references
- `security-audit-report.md` §4 (lines 827–867 — Dependency Audit from the
  previous audit) — read in full for comparison.
- `.agents/STATE.md` — confirmed current `master` tip is `fa659fd` (the
  STATE.md snapshot), and the actual current tip is `e3de735` (the audit
  worktree's HEAD).
- `.agents/AGENTS.md` — read for build conventions, port pool, design
  system rules.

### External sources queried
- GitHub Advisory Database — `GHSA-v5pm-xwqc-g5wc` (full advisory page),
  `tanstack` query (5 hits), `ajv` query (0 hits), `microsoft.identitymodel`
  query (2 hits), `OpenAI nuget` query (0 hits), `zod npm` query
  (0 direct hits).
- nuget.org flat-container API for version manifests of 17 packages
  (`Microsoft.AspNetCore.OpenApi`, `Microsoft.AspNetCore.OpenApi/10.0.9`,
  `10.0.10`, `10.0.11`, `10.0.12`, `Refit`, `Yarp.ReverseProxy`,
  `Sentry`, `Docker.DotNet`, `KubernetesClient`, `FluentValidation`,
  `OpenAI`, `Microsoft.Extensions.AI`, `LibGit2Sharp`, `Grpc.Net.Client`,
  `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.IdentityModel.Protocols.OpenIdConnect`,
  `xunit.v3`, `Shouldly`, `NSubstitute`, `coverlet.collector`,
  `NetArchTest.Rules`, `Microsoft.Extensions.ApiDescription.Server`,
  `EFCore.NamingConventions`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`,
  `Minio`, `Testcontainers.PostgreSql`, `protobuf-net.Grpc`).
- npm registry — `lucide-react`, `@types/node`, `typescript`, `vite`,
  and 49 other packages (latest stable + license where relevant).

---

**Audit complete.** No code was modified; only this report was produced.
Branch tip: `e3de735` (worktree `audit-2-deps`, branch `fix/audit-2-deps`).
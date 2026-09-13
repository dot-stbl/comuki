# Releases

How Comuki is versioned, what a tag publishes, and the host/worker
version contract. The deployment paths themselves live in
[`deploy/oss/README.md`](deploy/oss/README.md).

## Versioning scheme

- **`v0.N.x` — burn-in.** Anything may change between minor versions,
  including breaking changes (semver 0.x convention): database schema,
  worker protocol, API surface. Upgrade by reading the release notes,
  not by assuming drop-in compatibility.
- **`v1.0.0` — first public stable.** From there on semver applies in
  full: breaking changes require a major bump.

Git tags are always `vX.Y.Z` (with the leading `v`); the matching
container image tags are published **both** with and without the `v`
(see the table below).

## What a tag publishes

Pushing `v0.1.0` triggers [`.github/workflows/release.yml`](.github/workflows/release.yml),
which builds and pushes **both** public images from the same commit:

| Image | Contents | Tags |
|---|---|---|
| `ghcr.io/dot-stbl/comuki` | host + migrator + brain + dashboard SPA (one image, three entrypoints) | `v0.1.0`, `0.1.0`, `latest` |
| `ghcr.io/dot-stbl/comuki-worker` | translator + pi agent runtime (spawned per work item) | `v0.1.0`, `0.1.0`, `latest` |

There is no separate dashboard image — the SPA is baked into the host
(`/app/host/wwwroot`, same origin, cookies just work).

Two tags per version is deliberate:

- `v0.1.0` — verbatim git tag, what humans type;
- `0.1.0` — the semver, **and the tag the host derives for worker
  spawns** (below). It exists so the pin never 404s.

Every push to `master` (path-filtered to `platform/`, `agents/`,
`dashboard/`, `deploy/`) publishes rolling **`edge-<shortsha>`** tags of
both images — newest code, no compatibility promise, unstamped binaries.

### GitHub Release notes

The workflow runs with `contents: read` only, so the GitHub Release
object is cut by the tag author, with notes auto-generated from commits:

```bash
gh release create vX.Y.Z --generate-notes
```

## The version stamping chain

`COMUKI_VERSION` build arg (both Dockerfiles) → `dotnet publish
-p:VersionPrefix=<semver>` → `AssemblyInformationalVersion` →
`ComukiBuildInfo` → `comuki version`, `/api/v1/version`, and the
worker-image pinning. Unstamped builds report **`0.0.0`** — that is the
repo-wide `VersionPrefix` default for local `dotnet build` / unstamped
image builds, never a real release.

## Upgrade contract: worker image version MUST match host version

The worker image is not independently versioned: `comuki-worker:X` is
built from the same commit as `comuki:X` and speaks the wire protocol of
exactly that version. Mixing versions (host `v0.2.0` + worker `v0.1.0`)
is unsupported and fails in the worker's claim/gRPC handshake.

Enforcement is by default, not by prohibition:

- `COMUKI_COMPUTE_SCALE_WORKERIMAGE` with **no tag** (the shipped
  default `ghcr.io/dot-stbl/comuki-worker`) → the scale supervisor pins
  it to its own build version at spawn time (`WorkerImagePinning`,
  `platform/src/engine/Comuki.Engine.Compute`). A `v0.2.0` host spawns
  `comuki-worker:0.2.0` workers with zero configuration.
- Unstamped builds (`0.0.0` — local `dotnet build`, unstamped images)
  fall back to `:latest`.
- An **explicit tag or digest always wins** — operators pinning
  `registry.internal/comuki-worker@sha256:…` keep full control.

Edge images (`edge-<sha>`) are unstamped by design: they fall back to
`latest` workers unless you set `COMUKI_COMPUTE_SCALE_WORKERIMAGE`
explicitly.

## Two registries, on purpose

- **GHCR (`ghcr.io/dot-stbl/*`)** — the public face, built by GitHub
  Actions from this workflow. OSS self-hosting consumes these.
- **Harbor (`registry.hybrid.ai/*`)** — the corp mirror, built by GitLab
  CI with `:sha` / `:$CI_COMMIT_TAG` tags. The corp contour (helm
  releases, dev environments) consumes these.

The dual-build is deliberate, not duplication to be deduplicated: the
corp image vendors offline dependencies (pi, zod) for an
egress-restricted runner; the public image fetches them from npm at
build time.

## Cutting a release

1. Master is green (CI = `dotnet build` + test suites).
2. `git tag v0.N.0 && git push origin v0.N.0` — the workflow builds,
   pushes and smoke-tests both images (~20–40 min).
3. `gh release create v0.N.0 --generate-notes`.
4. Verify the packages landed public:
   <https://github.com/orgs/dot-stbl/packages> (first push of a package
   may need a one-time visibility flip to Public in package settings —
   `GITHUB_TOKEN` needs no extra secrets, but package visibility
   defaults to private for private source repos).

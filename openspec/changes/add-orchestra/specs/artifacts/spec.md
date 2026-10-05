## MODIFIED Requirements

### Requirement: Worker artifact upload extends its mime allow-list

The worker artifact upload endpoint (`POST /workers/{workItemId}/artifacts`) SHALL accept the mime types `image/png`, `text/html`, `image/svg+xml` (the three already on the `VisualArtifactLimits.MaxBytesByMime` allow-list) **and** `text/x-diff`. Bundles are stored under the `{project}/{run}/` MinIO prefix; the named key `cmdiff` points to the diff member.

> **Coordination note (2026-10-04).** The `text/x-diff` extension is added by the **verification** phase to support the `cmdiff` evidence pointer in the verification view. The existing worker-side bundle, the existing MinIO prefix, and the existing `VisualArtifactLimits` mime allow-list are unchanged — `text/x-diff` is added on top of the three already-allowed mimes.

#### Scenario: Worker uploads a changeset diff

- **WHEN** a worker posts `text/x-diff` content with `name = "changeset.diff"` and the bound work item
- **THEN** the bundle member lands in `{project}/{run}/changeset.diff` and the `artifacts.cmdiff` row points to it

#### Scenario: Disallowed mime refused

- **WHEN** a worker posts an unsupported mime (e.g. `application/x-msdownload` or `text/plain`)
- **THEN** the upload answers `415 Unsupported Media Type` and nothing is stored

### Requirement: cmdiff is a typed evidence pointer

The verification view (`GET /api/v1/runs/{runId}/verification`, `verification` capability) reads the `cmdiff` member of the bundle as the textual evidence a gate provider may consult. The canonical URI is the same one the `run.artifacts_bundled` event carries; no new artifact endpoint is added.

#### Scenario: Verification view reads cmdiff

- **WHEN** an authorised caller GETs `/runs/{runId}/verification` for a run whose bundle has a `cmdiff` member
- **THEN** the per-gate `evidence` array carries `{ kind: "cmdiff", uri: "<canonical uri>" }`

#### Scenario: Verification view without cmdiff

- **WHEN** an authorised caller GETs the view for a run whose bundle has no `cmdiff` member
- **THEN** the per-gate `evidence` array omits the `cmdiff` entry; other kinds (when added) are unaffected

## ADAPTER Notes

The worker artifact upload endpoint today accepts exactly three mimes on its allow-list (`image/png`, `text/html`, `image/svg+xml`, per `VisualArtifactLimits.MaxBytesByMime`). The change adds `text/x-diff` to the upload path, and the per-mime size cap is the same as the existing three mimes (the diff bundle member is bounded by the same `VisualArtifactLimits` policy, extended with a `TextDiffMaxBytes` constant). The MinIO bundle prefix and the `run.artifacts_bundled` journal event are unchanged.
namespace Comuki.Shared.Contracts.Verification;

/// <summary>
/// One evidence pointer a gate provider attaches to a
/// <c>VerificationRecord</c>. The <see cref="Uri"/> is the canonical
/// bundle-member URI (MinIO http(s) URL the host can fetch) — the
/// <see cref="GateEvidenceKind"/> narrows the row so the view can
/// group by kind without parsing the URI.
/// </summary>
/// <param name="Kind">Evidence kind — see <see cref="GateEvidenceKind"/>.</param>
/// <param name="Uri">Canonical bundle-member URI (e.g. <c>https://minio/{bucket}/{projectId}/{runId}/changeset.diff</c>).</param>
public sealed record GateEvidenceRef(GateEvidenceKind Kind, Uri Uri);

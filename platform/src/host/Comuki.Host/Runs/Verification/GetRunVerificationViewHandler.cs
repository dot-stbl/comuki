using Comuki.Engine.Orchestration.Application.Verification;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Artifacts.Application.VisualArtifacts.Ports;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Host.Runs.Verification;

/// <summary>
/// Read-side handler behind <c>GET /api/v1/runs/{runId}/verification</c>
/// (add-orchestra §3 — Coda). Returns one <see cref="RunVerificationView"/>
/// per run — the flattened per-(work item, gate) verdict list — or
/// <c>null</c> when the run is out of scope (404 in the controller, by
/// the same convention as <see cref="GetRunDetailHandler"/>). A run
/// that has no verdicts yet reads as <see cref="RunVerificationView"/>
/// with an empty <c>Gates</c> list — the FE renders "no gates evaluated
/// yet" without a separate null path.
/// </summary>
/// <param name="db">Orchestration context — the run/work item + verification row reads share the same scoped unit-of-work.</param>
/// <param name="records">Per-scope <see cref="IVerificationRecordStore"/> for the verifications table.</param>
/// <param name="artifacts">Visual-artifact store — the handler reads <c>changeset.diff</c> rows so the view can surface them as the <c>cmdiff</c> evidence pointer the spec requires (artifacts spec "cmdiff is a typed evidence pointer").</param>
public sealed class GetRunVerificationViewHandler(
    OrchestrationDbContext db,
    IVerificationRecordStore records,
    IVisualArtifactStore artifacts)
{
    /// <summary>
    /// Resolves the run's work items, then reads every per-(work item,
    /// gate) verdict and flattens it to a single newest-first list. The
    /// scope query filter on <c>Runs</c> / <c>WorkItems</c> gives the
    /// 404-by-design behaviour for out-of-scope runs.
    /// </summary>
    /// <param name="runId">Run to read.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public async Task<RunVerificationView?> GetAsync(RunId runId, CancellationToken cancellationToken = default)
    {
        var runExists = await db.Runs
            .AsNoTracking()
            .AnyAsync(candidate => candidate.Id == runId, cancellationToken);
        if (!runExists)
        {
            return null;
        }

        var workItemIds = await db.WorkItems
            .AsNoTracking()
            .Where(item => item.RunId == runId)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

        if (workItemIds.Count == 0)
        {
            // No work items = no gates to evaluate. Both booleans
            // are false: nothing has been verified, and there is
            // nothing pending — the empty-gates branch reads as a
            // neutral "no annotations" pill on the FE.
            return new RunVerificationView(
                RunId: runId.Value,
                Verified: false,
                VerificationPending: false,
                Gates: []);
        }

        var rows = await records.ListByWorkItemsAsync(workItemIds, cancellationToken);

        // The view layer enriches every gate with the work item's
        // cmdiff bundle member (the canonical `changeset.diff` filename
        // the worker uploads with mime text/x-diff). Gate providers
        // stay clean of bundle knowledge — they stamp the verdict; the
        // view joins the bundle in. A work item without a cmdiff
        // simply omits the entry (the spec's "without cmdiff" scenario).
        var cmdiffByWorkItem = await GetRunVerificationViewMappings.LoadCmdiffByWorkItemAsync(
            artifacts,
            workItemIds,
            cancellationToken);

        var gates = rows
            .Select(row => new VerificationGateView(
                WorkItemId: row.WorkItemId,
                GateName: row.GateName,
                Verdict: row.Verdict.Value,
                EvidenceRefs: [.. GetRunVerificationViewMappings.ResolveEvidenceUris(row, cmdiffByWorkItem)],
                EvaluatedAt: row.EvaluatedAt,
                Evaluator: row.Evaluator))
            .ToArray();

        // Run-level booleans (add-orchestra §3 — Coda,
        // <c>verification/spec.md</c> Requirement "Verification view is
        // a derived read"). Both derive from the gate list alone —
        // the view is read-only and never touches a project-settings
        // store.
        //
        //   Verified            = gates.Count > 0 && gates.All(g => g.Verdict == "passed")
        //   VerificationPending = !verified && gates.Any(g => g.Verdict == "pending")
        //
        // The four states the FE renders map onto:
        //   - empty list    → verified=false, pending=false (no verdicts yet)
        //   - all passed    → verified=true,  pending=false  (all-passed scenario)
        //   - any pending   → verified=false, pending=true   (verification-pending scenario)
        //   - only failed   → verified=false, pending=false  (gate rejected; no Pending drill-down)
        var hasPassed = false;
        var hasPending = false;
        var hasFailed = false;
        foreach (var gate in gates)
        {
            switch (gate.Verdict)
            {
                case "passed": hasPassed = true; break;
                case "pending": hasPending = true; break;
                case "failed": hasFailed = true; break;
            }
        }

        var verified = gates.Length > 0 && hasPassed && !hasPending && !hasFailed;
        var verificationPending = !verified && hasPending;

        return new RunVerificationView(
            RunId: runId.Value,
            Verified: verified,
            VerificationPending: verificationPending,
            Gates: gates);
    }
}

/// <summary>
/// File-static helpers for the verification view handler (add-orchestra
/// §3 — Coda). The view is read-only and the helpers carry no state
/// of their own, so the file-scope keeps them off the public surface
/// of <c>Comuki.Host</c> without putting them on the handler class
/// (handlers do the orchestration; pure projections belong to a
/// file-static sibling per <c>class-layout-and-tooling.md</c> §1a).
/// </summary>
file static class GetRunVerificationViewMappings
{
    /// <summary>
    /// Canonical evidence filename the worker uploads through
    /// <c>POST /workers/{workItemId}/artifacts</c> with mime
    /// <c>text/x-diff</c>. The visual-artifact allow-list already
    /// accepts that mime (issue #51 slice 1 extended it; the verification
    /// axis lands on the same key). The view looks the row up by this
    /// filename and exposes its canonical URI as the
    /// <c>evidence[kind="cmdiff"]</c> entry on every gate the work item
    /// participates in.
    /// </summary>
    private const string ChangesetDiffFilename = "changeset.diff";

    /// <summary>
    /// Loads the run's per-(work item, cmdiff) bundle-member dictionary.
    /// A work item may carry several artifact versions with the same
    /// filename (the store keeps a monotonic version counter per id) —
    /// the <see cref="IVisualArtifactStore.ListByWorkItemsAndFilenameAsync"/>
    /// contract returns every version and the caller picks the first
    /// (the latest under the store's <c>OrderByDescending(Version)</c>).
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> LoadCmdiffByWorkItemAsync(
        IVisualArtifactStore artifacts,
        IReadOnlyList<Guid> workItemIds,
        CancellationToken cancellationToken)
    {
        // The bundle member the worker uploaded for a given work item
        // lives at Filename == "changeset.diff" (a single upload per
        // work item — the spec scopes the cmdiff to the worker, not the
        // run). The URI is the artifact id; the content proxy
        // translates that to a signed URL on read.
        var matches = await artifacts.ListByWorkItemsAndFilenameAsync(
            workItemIds,
            ChangesetDiffFilename,
            cancellationToken);

        return matches
            .Where(static match => match.WorkItemId.HasValue)
            .GroupBy(static match => match.WorkItemId!.Value)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().Id.ToString());
    }

    /// <summary>
    /// Resolves the URI list for one gate row. First whatever the gate
    /// provider stamped on the row, then the work item's cmdiff bundle
    /// member if present. The dictionary lookup is O(1); the view's
    /// flatten keeps the row list newest-first, so a re-evaluation
    /// supersedes prior evidence through the upsert on the underlying
    /// record.
    /// </summary>
    public static IEnumerable<string> ResolveEvidenceUris(
        Engine.Orchestration.Domain.Verification.VerificationRecord row,
        IReadOnlyDictionary<Guid, string> cmdiffByWorkItem)
    {
        foreach (var uri in row.EvidenceRefs.Select(static evidence => evidence.Uri.ToString()))
        {
            yield return uri;
        }

        if (cmdiffByWorkItem.TryGetValue(row.WorkItemId, out var cmdiffUri))
        {
            yield return cmdiffUri;
        }
    }
}

using Comuki.Modules.Memory.Application.Learning;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Ids;
using Comuki.Modules.Memory.Domain.Learning;
using Microsoft.EntityFrameworkCore;

namespace Comuki.Modules.Memory.Infrastructure.Persistence.Stores;

/// <summary>
/// EF/Npgsql implementation of <see cref="ILearningCandidateStore"/>. Every
/// method opens its own context from the factory (the store is a safe
/// singleton), the same posture as <see cref="EfMemoryStore"/>. Candidates
/// carry no ownership axis (a global operator queue, per
/// <see cref="MemoryDbContext"/>'s own remark) and so run unfiltered here —
/// the approvals surface's gate is the learning:read / learning:write
/// permission, not a subject-scope row filter.
/// </summary>
/// <param name="dbFactory"></param>
public sealed class EfLearningCandidateStore(IDbContextFactory<MemoryDbContext> dbFactory) : ILearningCandidateStore
{
    /// <inheritdoc />
    public async Task<LearningCandidateView> SuggestAsync(LearningSuggestion suggestion, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var topic = suggestion.Topic.Trim();
        var proposedRule = suggestion.ProposedRule.Trim();

        // A repeat, not a duplicate: the same (project, topic, rule) still
        // pending is evidence the pattern recurs — exactly what the repeat
        // counter exists to count. Decided candidates never match, so a
        // re-suggested rule starts a fresh review.
        var existing = await db.LearningCandidates
            .Where(candidate => candidate.Status == LearningStatus.Pending
                && candidate.ProjectId == suggestion.ProjectId
                && candidate.Topic == topic
                && candidate.ProposedRule == proposedRule)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is { } pending)
        {
            pending.RegisterRepeat();
            await db.SaveChangesAsync(cancellationToken);
            return LearningCandidateView.Of(pending);
        }

        var created = LearningCandidate.Create(
            suggestion.ProjectId,
            suggestion.Topic,
            suggestion.Observation,
            suggestion.ProposedRule,
            suggestion.SourceRef,
            now);
        db.LearningCandidates.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        return LearningCandidateView.Of(created);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LearningCandidateView>> ListAsync(
        LearningStatus? status = null,
        int limit = ILearningCandidateStore.DefaultListLimit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var rows = await db.LearningCandidates
            .AsNoTracking()
            .Where(candidate => status == null || candidate.Status == status)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(LearningCandidateView.Of)];
    }

    /// <inheritdoc />
    public async Task<LearningCandidateView?> GetAsync(LearningCandidateId id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var candidate = await db.LearningCandidates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return candidate is null ? null : LearningCandidateView.Of(candidate);
    }

    /// <inheritdoc />
    public async Task<LearningCandidateView?> ApproveAsync(LearningCandidateId id, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        if (await db.LearningCandidates.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken) is not { } candidate)
        {
            return null;
        }

        // already-approved re-enters as the publish-retry path; only a
        // rejected candidate is a genuine conflict
        if (candidate.Status == LearningStatus.Pending)
        {
            candidate.Approve(now);
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (candidate.Status != LearningStatus.Approved)
        {
            throw new LearningDecisionConflictException(candidate.Id, candidate.Status);
        }

        return LearningCandidateView.Of(candidate);
    }

    /// <inheritdoc />
    public async Task<LearningCandidateView?> RejectAsync(LearningCandidateId id, DateTimeOffset now, string? reason = null, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        if (await db.LearningCandidates.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken) is not { } candidate)
        {
            return null;
        }

        if (candidate.Status != LearningStatus.Pending)
        {
            throw new LearningDecisionConflictException(candidate.Id, candidate.Status);
        }

        candidate.Reject(now, reason);
        await db.SaveChangesAsync(cancellationToken);
        return LearningCandidateView.Of(candidate);
    }
}

using FluentValidation;

namespace Comuki.Host.Runs.Models;

/// <summary>
/// Body of <c>POST /api/v1/runs/{runId}/steer</c> (add-orchestra §1 — Baton).
/// Carries the operator's steer text — the same text the live
/// <c>WorkerCommandHandler</c> appends to the worker's
/// <c>comuki-injected-context.md</c>, and the same text the Phase 1a
/// follow-up WorkItem's <c>brief</c> reproduces. The follow-up is the
/// no-LiveSession runtime's only way to surface the steer to a fresh
/// worker after the reaper reclaims a dead lease.
/// </summary>
public sealed class SteerRunRequest
{
    /// <summary>The operator's steer text. Required; non-empty after trim.</summary>
    public string Text { get; init; } = string.Empty;
}

/// <summary>Validation of <see cref="SteerRunRequest"/>.</summary>
public sealed class SteerRunRequestValidator : AbstractValidator<SteerRunRequest>
{
    /// <summary>
    /// Rule: <c>Text</c> must be a non-empty, non-whitespace string —
    /// a steer has no input to forward otherwise. The
    /// <c>WithErrorCode("steer.text_required")</c> on the failure keeps
    /// the wire contract identical to the previous controller-side
    /// guard: a 400 <c>application/problem+json</c> whose
    /// <c>extensions.code</c> is <c>steer.text_required</c>.
    /// </summary>
    public SteerRunRequestValidator()
    {
        RuleFor(static request => request.Text)
            .NotEmpty()
            .WithErrorCode("steer.text_required")
            .WithMessage("the operator's steer text must be a non-empty string");
    }
}

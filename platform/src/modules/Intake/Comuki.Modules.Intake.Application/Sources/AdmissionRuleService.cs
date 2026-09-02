using Comuki.Modules.Intake.Application.Ports;
using Comuki.Modules.Intake.Application.Views;
using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Modules.Intake.Domain.Rules;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Intake.Application.Sources;

/// <summary>
/// CRUD service for admission rules — the per-project watch/inbox
/// configuration the webhook pipeline consults.
/// </summary>
/// <param name="store"></param>
/// <param name="clock"></param>
/// <param name="validator"></param>
/// <param name="logger"></param>
public sealed class AdmissionRuleService(
    IIntakeStore store,
    TimeProvider clock,
    IValidator<CreateAdmissionRuleCommand> validator,
    ILogger<AdmissionRuleService> logger)
{
    /// <summary>Creates a rule.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<AdmissionRuleView> CreateAsync(CreateAdmissionRuleCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var mode = command.Mode == "watch" ? AdmissionMode.Watch : AdmissionMode.Inbox;
        var rule = AdmissionRule.Create(command.ProjectId, mode, command.FilterJson.Trim(), clock.GetUtcNow());

        await store.AddRuleAsync(rule, cancellationToken);
        logger.LogInformation("Admission rule {RuleId} created ({Mode}) for project {ProjectId}", rule.Id, mode, command.ProjectId);

        return AdmissionRuleView.Of(rule);
    }

    /// <summary>Lists rules, optionally per project.</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<IReadOnlyList<AdmissionRuleView>> ListAsync(ProjectId? projectId, CancellationToken cancellationToken = default)
    {
        var rules = await store.ListRulesAsync(projectId, cancellationToken);
        return [.. rules.Select(AdmissionRuleView.Of)];
    }

    /// <summary>Reads one rule.</summary>
    /// <param name="ruleId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="AdmissionRuleNotFoundException">Unknown id.</exception>
    public async Task<AdmissionRuleView> GetAsync(AdmissionRuleId ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await store.FindRuleAsync(ruleId, cancellationToken)
            ?? throw new AdmissionRuleNotFoundException(ruleId);

        return AdmissionRuleView.Of(rule);
    }

    /// <summary>Partial update (PATCH semantics — null fields stay).</summary>
    /// <param name="ruleId"></param>
    /// <param name="mode"></param>
    /// <param name="filterJson"></param>
    /// <param name="enabled"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="AdmissionRuleNotFoundException">Unknown id.</exception>
    public async Task<AdmissionRuleView> UpdateAsync(
        AdmissionRuleId ruleId,
        string? mode,
        string? filterJson,
        bool? enabled,
        CancellationToken cancellationToken = default)
    {
        var rule = await store.FindRuleAsync(ruleId, cancellationToken)
            ?? throw new AdmissionRuleNotFoundException(ruleId);

        AdmissionMode? parsedMode = mode switch
        {
            "watch" => AdmissionMode.Watch,
            "inbox" => AdmissionMode.Inbox,
            null => null,
            _ => throw new ValidationException("mode must be 'watch' or 'inbox'"),
        };

        rule.Update(parsedMode, filterJson, enabled, clock.GetUtcNow());
        await store.UpdateRuleAsync(rule, cancellationToken);
        logger.LogInformation("Admission rule {RuleId} updated", ruleId);

        return AdmissionRuleView.Of(rule);
    }

    /// <summary>Deletes a rule (idempotent).</summary>
    /// <param name="ruleId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task DeleteAsync(AdmissionRuleId ruleId, CancellationToken cancellationToken = default)
    {
        return store.DeleteRuleAsync(ruleId, cancellationToken);
    }
}

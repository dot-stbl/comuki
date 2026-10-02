using Comuki.Modules.Procedures.Application.Patches.Chat;
using FluentValidation;
namespace Comuki.Host.Procedures.Models;

/// <summary>
/// Request DTO for proposing a patch from chat
/// (<c>POST /api/v1/procedures/{projectId}/{procedureKey}/propose-patch</c>).
/// The chat surface calls this when an operator asks for a procedure
/// change in chat — the brain drafts a <c>GraphPatch</c> via the
/// <see cref="ProposePatchFromChatHandler"/>
/// and the human publishes it from Studio.
/// </summary>
public sealed record ProposePatchRequest(
    string BaseVersionId,
    string Rationale,
    string DraftedBy);

/// <summary>
/// FluentValidation rules for <see cref="ProposePatchRequest"/>: all
/// three fields are user-editable chat input and must be non-empty;
/// the base version id is a 64-char hex content address.
/// </summary>
public sealed class ProposePatchRequestValidator : AbstractValidator<ProposePatchRequest>
{
    /// <summary>Declares the field rules for the propose-patch body.</summary>
    public ProposePatchRequestValidator()
    {
        RuleFor(static request => request.BaseVersionId)
            .NotEmpty()
            .Matches("^[0-9a-f]{64}$")
            .WithMessage("BaseVersionId must be a 64-character lowercase hex content address.");

        RuleFor(static request => request.Rationale)
            .NotEmpty()
            .MaximumLength(2000);

        RuleFor(static request => request.DraftedBy)
            .NotEmpty()
            .MaximumLength(200);
    }
}

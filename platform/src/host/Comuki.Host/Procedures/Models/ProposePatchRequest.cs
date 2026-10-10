using System.Text.Json;
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
/// <param name="BaseVersionId">The content-addressed compiled version the brain is patching against.</param>
/// <param name="Rationale">Free-form explanation of the patch's intent (operator-visible).</param>
/// <param name="DraftedBy">Identity of the brain session that produced the draft (server-stamps the kind).</param>
/// <param name="Operations">
/// The patch's typed operations in order. Each element is a
/// <see cref="JsonElement"/> with a <c>kind</c> discriminator
/// (<c>add-node</c> / <c>remove-node</c> / <c>rewire-edge</c> /
/// <c>re-parameterize-node</c>) and a body that
/// <see cref="Modules.Procedures.Application.Patches.Drafting.GraphPatchOperationJsonConverter"/>
/// deserializes into the closed <c>GraphPatchOperation</c> hierarchy.
/// Default is an empty array — the controller's previous behavior (a
/// no-op patch with <c>unchanged: true</c>) stays reachable for
/// first-cut studio requests that just want the diff surface without
/// writing operations yet.
/// </param>
public sealed record ProposePatchRequest(
    string BaseVersionId,
    string Rationale,
    string DraftedBy,
    IReadOnlyList<JsonElement> Operations);

/// <summary>
/// FluentValidation rules for <see cref="ProposePatchRequest"/>: the three
/// legacy fields are user-editable chat input and must be non-empty;
/// the base version id is a 64-char hex content address. Operations
/// are not validated here — the converter raises a typed
/// <see cref="JsonException"/> on an unknown kind,
/// which the controller's exception handler maps to 400 via
/// <c>AddProblemDetails()</c>.
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

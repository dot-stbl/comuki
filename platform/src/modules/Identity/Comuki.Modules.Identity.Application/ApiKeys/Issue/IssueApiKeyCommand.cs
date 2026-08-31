using Comuki.Modules.Identity.Domain.Ids;

namespace Comuki.Modules.Identity.Application.ApiKeys.Issue;

/// <summary>Issues an API key for an existing, enabled account.</summary>
/// <param name="UserId"></param>
/// <param name="Name"></param>
public sealed record IssueApiKeyCommand(UserId UserId, string Name);

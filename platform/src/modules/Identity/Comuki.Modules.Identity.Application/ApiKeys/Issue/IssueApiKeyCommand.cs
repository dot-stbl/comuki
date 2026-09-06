using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Identity.Application.ApiKeys.Issue;

/// <summary>Issues an API key for an existing, enabled account.</summary>
/// <param name="UserId"></param>
/// <param name="Name"></param>
/// <param name="TenantProjectId">
/// Optional tenant scope. When set, the key only authenticates
/// requests that carry the matching <c>X-Comuki-Tenant</c> header.
/// </param>
public sealed record IssueApiKeyCommand(UserId UserId, string Name, ProjectId? TenantProjectId = null);

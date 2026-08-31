using Comuki.Modules.Identity.Domain.Ids;

namespace Comuki.Modules.Identity.Application.Views;

/// <summary>Read model of a user account — no secrets ride along.</summary>
/// <param name="Id"></param>
/// <param name="Email"></param>
/// <param name="DisplayName"></param>
/// <param name="Disabled"></param>
/// <param name="TokensVersion"></param>
/// <param name="CreatedAt"></param>
public sealed record UserAccountView(
    UserId Id,
    string Email,
    string DisplayName,
    bool Disabled,
    int TokensVersion,
    DateTimeOffset CreatedAt);

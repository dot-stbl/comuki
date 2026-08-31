namespace Comuki.Host.Auth.Models;

/// <summary>Body of a successful login — the session itself lives in the cookie.</summary>
/// <param name="UserId"></param>
/// <param name="Email"></param>
/// <param name="DisplayName"></param>
public sealed record LoginResponse(Guid UserId, string Email, string DisplayName);

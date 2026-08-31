namespace Comuki.Host.Auth.Models;

/// <summary>Email+password login body.</summary>
/// <param name="Email"></param>
/// <param name="Password"></param>
public sealed record LoginRequest(string Email, string Password);

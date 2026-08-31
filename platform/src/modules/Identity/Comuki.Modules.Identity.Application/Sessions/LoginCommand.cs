namespace Comuki.Modules.Identity.Application.Sessions;

/// <summary>Local password login.</summary>
/// <param name="Email"></param>
/// <param name="Password"></param>
public sealed record LoginCommand(string Email, string Password);

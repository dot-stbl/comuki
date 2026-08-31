using Comuki.Modules.Identity.Application.Views;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// The linker's answer: the local account the external identity maps to,
/// and whether it had to be created. The IdP never decides permissions —
/// assignments stay in Comuki either way.
/// </summary>
/// <param name="User"></param>
/// <param name="Created">True when a new account was provisioned.</param>
public sealed record OidcLinkResult(UserAccountView User, bool Created);

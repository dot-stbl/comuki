using Comuki.Modules.Identity.Domain.Ids;

namespace Comuki.Modules.Identity.Domain.Subjects;

/// <summary>
/// A reference to whoever holds assignments — a user or an API key —
/// independent of how they authenticated. Users grant and receive roles;
/// API keys receive roles (and act under their owner's principal).
/// </summary>
/// <param name="Type"></param>
/// <param name="Id">The user id or api key id as a raw Guid.</param>
public readonly record struct RoleSubject(SubjectType Type, Guid Id)
{
    /// <summary>Creates a user subject.</summary>
    public static RoleSubject ForUser(UserId userId)
    {
        return new RoleSubject(SubjectType.User, userId.Value);
    }

    /// <summary>Creates an API-key subject.</summary>
    public static RoleSubject ForApiKey(ApiKeyId apiKeyId)
    {
        return new RoleSubject(SubjectType.ApiKey, apiKeyId.Value);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{SubjectTypeKeys.Key(Type)}:{Id}";
    }
}

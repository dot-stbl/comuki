namespace Comuki.Modules.Identity.Domain.Subjects;

/// <summary>Stable stored keys of <see cref="SubjectType"/>.</summary>
public static class SubjectTypeKeys
{
    /// <summary>Key of <see cref="SubjectType.User"/>.</summary>
    public const string User = "user";

    /// <summary>Key of <see cref="SubjectType.ApiKey"/>.</summary>
    public const string ApiKey = "api-key";

    /// <summary>Returns the key of a subject type; total over the enum.</summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static string Key(SubjectType type)
    {
        return type switch
        {
            SubjectType.User => User,
            SubjectType.ApiKey => ApiKey,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}

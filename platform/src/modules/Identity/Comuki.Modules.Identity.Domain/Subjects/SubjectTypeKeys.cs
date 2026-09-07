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

    /// <summary>Parses a wire-side key into the enum; returns <c>null</c> when the input is unknown.</summary>
    /// <param name="key">The wire key (<c>"user"</c> or <c>"api-key"</c>); <c>null</c> returns <c>null</c>.</param>
    public static SubjectType? Parse(string? key)
    {
        return key switch
        {
            null => null,
            User => SubjectType.User,
            ApiKey => SubjectType.ApiKey,
            _ => null,
        };
    }
}

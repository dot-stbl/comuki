namespace Comuki.Modules.Identity.Domain.Subjects;

/// <summary>
/// The kind of principal an assignment or authentication belongs to. The
/// way a caller authenticates (cookie or API key) does not change the
/// permission model — both resolve to the same subject space.
/// </summary>
public enum SubjectType
{
    /// <summary>An interactive user account.</summary>
    User = 0,

    /// <summary>An API key — a first-class automation subject.</summary>
    ApiKey = 1,
}

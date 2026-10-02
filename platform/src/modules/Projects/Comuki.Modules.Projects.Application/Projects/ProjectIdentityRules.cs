using System.Text.RegularExpressions;
using Comuki.Modules.Projects.Domain.Projects;

namespace Comuki.Modules.Projects.Application.Projects;

/// <summary>
/// Identity-field rule predicates shared by the create and update
/// validators (design D4): normalisation lives in the domain, so these
/// check only what normalisation cannot fix — the tag pattern after
/// trimming, the distinct tag count, the colour shape and (via the
/// validators themselves) the icon bound.
/// </summary>
internal static partial class ProjectIdentityRules
{
    /// <summary>Compiled <see cref="Project.TagPattern"/>.</summary>
    [GeneratedRegex(Project.TagPattern)]
    public static partial Regex TagPattern();

    /// <summary>Compiled <see cref="Project.ColorPattern"/>.</summary>
    [GeneratedRegex(Project.ColorPattern)]
    public static partial Regex ColorPattern();

    /// <summary>Compiled <see cref="Project.EnvClassPattern"/>.</summary>
    [GeneratedRegex(Project.EnvClassPattern)]
    public static partial Regex EnvClassPattern();

    /// <summary>
    /// True when every tag, after the domain's normalisation (trim,
    /// lower-case, drop blanks, de-duplicate), matches
    /// <see cref="Project.TagPattern"/> and the distinct list fits
    /// <see cref="Project.MaxTags"/>.
    /// </summary>
    public static bool TagsAreWellFormed(IReadOnlyList<string> tags)
    {
        var normalized = Project.NormalizeTags(tags);

        return normalized.Length <= Project.MaxTags
            && normalized.All(static tag => TagPattern().IsMatch(tag));
    }

    /// <summary>True when the colour, after the domain's trim, is a #rrggbb hex in either letter case.</summary>
    public static bool ColorIsWellFormed(string color)
    {
        return ColorPattern().IsMatch(color.Trim());
    }

    /// <summary>
    /// True when the environment-class id matches
    /// <see cref="Project.EnvClassPattern"/> after the domain's trim
    /// (catalog ids are lower-case alphanumeric + dash by convention;
    /// see <c>Comuki.Engine.Compute.Environments.EnvironmentBundle</c>).
    /// Empty string is rejected — use <c>null</c> to clear.
    /// </summary>
    public static bool EnvClassIsWellFormed(string envClass)
    {
        return EnvClassPattern().IsMatch(envClass.Trim());
    }
}

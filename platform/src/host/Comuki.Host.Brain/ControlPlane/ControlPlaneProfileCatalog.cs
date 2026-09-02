using Comuki.Host.Brain.Brain.Options;
using Comuki.Shared.Contracts.ControlPlane.Profiles;

namespace Comuki.Host.Brain.ControlPlane;

/// <summary>
/// Brain-side profile catalog over the control-plane <c>profiles/</c>
/// folder: one markdown document per profile, frontmatter carrying
/// name / description / allowedTools / model, file stem = key. A missing
/// folder answers an empty catalog (warned) — the brain stays bootable on
/// a bare checkout. Malformed documents are skipped, not fatal — the same
/// contract as the orchestrator-side catalog.
/// </summary>
/// <param name="options"></param>
/// <param name="logger"></param>
public sealed class ControlPlaneProfileCatalog(
    BrainOptions options,
    ILogger<ControlPlaneProfileCatalog> logger) : IProfileCatalog
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ProfileDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ControlPlaneProfileReader.Scan(options.ControlPlaneProfilesPath, logger));
    }

    /// <inheritdoc />
    public async Task<ProfileDefinition?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var profiles = await ListAsync(cancellationToken);
        return profiles.FirstOrDefault(profile => profile.Key == key);
    }
}

/// <summary>Scans the profiles folder; IO + parse, no state.</summary>
file static class ControlPlaneProfileReader
{
    public static IReadOnlyList<ProfileDefinition> Scan(string profilesPath, ILogger logger)
    {
        if (!Directory.Exists(profilesPath))
        {
            logger.LogWarning(
                "control-plane profiles folder '{ProfilesPath}' not found; profile catalog is empty",
                profilesPath);
            return [];
        }

        var profiles = new List<ProfileDefinition>();
        foreach (var file in Directory.EnumerateFiles(profilesPath, "*.md", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            if (ProfileDocumentParser.Parse(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file)) is { } profile)
            {
                profiles.Add(profile);
            }
            else
            {
                logger.LogWarning("profile document '{ProfileFile}' is malformed; skipped", file);
            }
        }

        return profiles;
    }
}

/// <summary>
/// Minimal frontmatter reader for profile documents: name, description,
/// allowedTools (yaml list), model. Returns null when the frontmatter
/// lacks a name or description.
/// </summary>
file static class ProfileDocumentParser
{
    public static ProfileDefinition? Parse(string key, string document)
    {
        var frontmatter = FrontmatterOf(document);
        if (frontmatter is null)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tools = new List<string>();
        var collectingTools = false;
        foreach (var rawLine in frontmatter.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // block-list item: belongs to the preceding key (allowedTools:
            // followed by "  - Read" lines — the shape real profile
            // documents use)
            if (line.StartsWith('-'))
            {
                var item = line.TrimStart('-').Trim();
                if (collectingTools && item.Length > 0)
                {
                    tools.Add(item);
                }

                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            collectingTools = false;
            if (name.Equals("allowedTools", StringComparison.OrdinalIgnoreCase))
            {
                // empty value opens a block list below; non-empty is an
                // inline comma list (allowedTools: Read, Bash)
                collectingTools = value.Length == 0;
                foreach (var item in value.Split(','))
                {
                    var trimmed = item.Trim();
                    if (trimmed.Length > 0)
                    {
                        tools.Add(trimmed);
                    }
                }
            }
            else
            {
                values[name] = value;
            }
        }

        return values.TryGetValue("name", out var profileName)
            && values.TryGetValue("description", out var description)
            && !string.IsNullOrWhiteSpace(profileName)
            && !string.IsNullOrWhiteSpace(description)
            ? new ProfileDefinition(
                key,
                profileName,
                description,
                tools,
                values.TryGetValue("model", out var model) && model.Length > 0 ? model : null)
            : null;
    }

    public static string? FrontmatterOf(string document)
    {
        var trimmed = document.TrimStart();
        if (!trimmed.StartsWith("---", StringComparison.Ordinal))
        {
            return null;
        }

        var end = trimmed.IndexOf("\n---", StringComparison.Ordinal);
        return end > 0 ? trimmed[3..end].Trim('\r', '\n') : null;
    }
}

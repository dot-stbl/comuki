using Comuki.Host.ControlPlane.Parsing;
using Comuki.Shared.Contracts.ControlPlane.ChatCommands;
using Comuki.Shared.Contracts.ControlPlane.Profiles;
using Microsoft.Extensions.Options;

namespace Comuki.Host.ControlPlane;

/// <summary>
/// File-backed control-plane catalog: reads <c>profiles/</c> and
/// <c>chat-commands/</c> markdown documents from the control-plane root -
/// the repo default content in development, a mounted client overlay in
/// deployment. One service implements both catalog ports: the folders share
/// the document format and the reading loop, while the ports stay separate
/// so each consumer depends only on the surface it uses (brain/profiles vs
/// chat harness/commands). Reads happen per call; caching joins the catalog
/// slice that needs it.
/// </summary>
public sealed class ControlPlaneCatalog(
    IOptions<ControlPlaneCatalogOptions> options,
    ILogger<ControlPlaneCatalog> logger) : IProfileCatalog, IChatCommandCatalog
{
    /// <summary>Folder name of worker profiles inside the control-plane root.</summary>
    public const string ProfilesFolder = "profiles";

    /// <summary>Folder name of the built-in chat-command pack inside the control-plane root.</summary>
    public const string ChatCommandsFolder = "chat-commands";

    /// <summary>Directory name the root probe looks for.</summary>
    public const string RootFolderName = "control-plane";

    private const int ProbeDepthLimit = 8;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProfileDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entries = await ControlPlaneFiles.LoadMarkdownDocumentsAsync(
            options.Value.Root ?? ProbeControlPlaneRoot(AppContext.BaseDirectory),
            ProfilesFolder,
            logger,
            cancellationToken);

        return [.. entries
            .Select(static entry => new ProfileDefinition(
                entry.Key,
                entry.Document.Name,
                entry.Document.Description,
                entry.Document.AllowedTools,
                entry.Document.Model))
            .OrderBy(static profile => profile.Key, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public async Task<ProfileDefinition?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var profiles = await ListAsync(cancellationToken);

        return profiles.FirstOrDefault(profile =>
            string.Equals(profile.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatCommandDefinition>> ListCommandsAsync(CancellationToken cancellationToken = default)
    {
        var entries = await ControlPlaneFiles.LoadMarkdownDocumentsAsync(
            options.Value.Root ?? ProbeControlPlaneRoot(AppContext.BaseDirectory),
            ChatCommandsFolder,
            logger,
            cancellationToken);

        return [.. entries
            .Select(static entry => new ChatCommandDefinition(
                entry.Key,
                entry.Document.Name,
                entry.Document.Description,
                entry.Document.Body))
            .OrderBy(static command => command.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Walks upward from <paramref name="startDirectory"/> looking for a
    /// directory named <c>control-plane</c> - in a dev checkout the host
    /// binaries sit several levels below the repo root. Bounded by a depth
    /// limit; returns null when not found.
    /// </summary>
    /// <param name="startDirectory"></param>
    public static string? ProbeControlPlaneRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        for (var depth = 0; depth < ProbeDepthLimit && directory is not null; depth++)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, RootFolderName)))
            {
                return Path.Combine(directory.FullName, RootFolderName);
            }

            directory = directory.Parent;
        }

        return null;
    }
}

/// <summary>Directory reading shared by both catalog surfaces: every .md file under one control-plane folder.</summary>
file static class ControlPlaneFiles
{
    public static async Task<List<ControlPlaneCatalogEntry>> LoadMarkdownDocumentsAsync(
        string? root,
        string folderName,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var entries = new List<ControlPlaneCatalogEntry>();

        if (root is null)
        {
            logger.LogWarning("Control-plane root not found; {Folder} catalog is empty", folderName);
            return entries;
        }

        var folder = Path.Combine(root, folderName);
        if (!Directory.Exists(folder))
        {
            return entries;
        }

        foreach (var filePath in Directory.EnumerateFiles(folder, "*.md", SearchOption.TopDirectoryOnly))
        {
            var document = ControlPlaneDocumentParser.Parse(
                await File.ReadAllTextAsync(filePath, cancellationToken));
            if (document is null)
            {
                logger.LogWarning(
                    "Skipping {FilePath}: missing or invalid frontmatter (name and description required)",
                    filePath);
                continue;
            }

            entries.Add(new ControlPlaneCatalogEntry(Path.GetFileNameWithoutExtension(filePath), document));
        }

        return entries;
    }
}

/// <summary>One catalog entry: the document plus its stable key (the file stem).</summary>
/// <param name="Key"></param>
/// <param name="Document"></param>
file sealed record ControlPlaneCatalogEntry(string Key, ControlPlaneDocument Document);

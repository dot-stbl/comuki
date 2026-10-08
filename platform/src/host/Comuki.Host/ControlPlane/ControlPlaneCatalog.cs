using Comuki.Host.ControlPlane.Parsing;
using Comuki.Shared.Contracts.ControlPlane.ChatCommands;
using Comuki.Shared.Contracts.ControlPlane.Profiles;
using Comuki.Shared.Contracts.ControlPlane.Skills;
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
        var entries = await ControlPlaneDocumentLoader.LoadAsync(
            options.Value.Root ?? ProbeControlPlaneRoot(AppContext.BaseDirectory),
            ProfilesFolder,
            ControlPlaneDocumentLoader.EnumerateMarkdownFiles,
            ControlPlaneDocumentParser.Parse,
            (source, document) => new ProfileDefinition(
                source.Key,
                document.Name,
                document.Description,
                document.AllowedTools,
                document.Model),
            logger,
            cancellationToken);

        return [.. entries
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
        var entries = await ControlPlaneDocumentLoader.LoadAsync(
            options.Value.Root ?? ProbeControlPlaneRoot(AppContext.BaseDirectory),
            ChatCommandsFolder,
            ControlPlaneDocumentLoader.EnumerateMarkdownFiles,
            ControlPlaneDocumentParser.Parse,
            (source, document) => new ChatCommandDefinition(
                source.Key,
                document.Name,
                document.Description,
                document.Body),
            logger,
            cancellationToken);

        return [.. entries
            .OrderBy(static command => command.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Walks upward from <paramref name="startDirectory"/> looking for a
    /// directory named <c>control-plane</c> - in a dev checkout the host
    /// binaries sit several levels below the repo root. Bounded by a depth
    /// limit; returns null when not found.
    /// </summary>
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

/// <summary>
/// Shared loader for the two control-plane catalog surfaces (profiles /
/// chat-commands) and the skill catalog. Owns the folder-resolution +
/// enumeration + per-document read + parse + log-and-skip invalid loop;
/// each catalog contributes only the per-shape enumeration strategy
/// (flat <c>*.md</c> for profiles/chat-commands, <c>SKILL.md</c> inside
/// one-level subdirectories for skills) and the per-shape entry
/// factory. Reused for both surfaces so a malformed document always
/// logs through the same warning text, and the <c>GetAsync</c> shape
/// (case-insensitive first-match) is identical across catalogs.
/// </summary>
internal static class ControlPlaneDocumentLoader
{
    /// <summary>One document candidate: the stable catalog key plus the absolute file to read.</summary>
    /// <param name="Key">Stable catalog key — file stem (ControlPlane) or subdirectory name (skills).</param>
    /// <param name="FilePath">Absolute path of the file to read.</param>
    public readonly record struct CatalogSource(string Key, string FilePath);

    /// <summary>Enumeration strategy: yield every candidate under the given folder.</summary>
    /// <param name="folder">The resolved folder to enumerate (caller has verified it exists).</param>
    public delegate IEnumerable<CatalogSource> EnumerateItems(string folder);

    /// <summary>Flat enumeration: every <c>*.md</c> in the folder (profiles, chat-commands).</summary>
    public static IEnumerable<CatalogSource> EnumerateMarkdownFiles(string folder)
    {
        foreach (var filePath in Directory.EnumerateFiles(folder, "*.md", SearchOption.TopDirectoryOnly))
        {
            yield return new CatalogSource(Path.GetFileNameWithoutExtension(filePath), filePath);
        }
    }

    /// <summary>
    /// Subdirectory enumeration: each child directory contributes one
    /// candidate named after the directory and pointing at a fixed file
    /// inside it (e.g. <c>SKILL.md</c>). Directories without the named
    /// file are skipped — the loader treats that as "not a candidate"
    /// rather than an error.
    /// </summary>
    /// <param name="folder"></param>
    /// <param name="fileName">Fixed file name to pair with each subdirectory (e.g. <c>SKILL.md</c>).</param>
    public static IEnumerable<CatalogSource> EnumerateSubdirectoryFile(string folder, string fileName)
    {
        foreach (var directoryPath in Directory.EnumerateDirectories(folder))
        {
            var filePath = Path.Combine(directoryPath, fileName);
            if (!File.Exists(filePath))
            {
                continue;
            }

            yield return new CatalogSource(Path.GetFileName(directoryPath), filePath);
        }
    }

    /// <summary>
    /// Shared read loop: resolve root → folder, enumerate the candidates,
    /// read each, parse, and skip invalid documents. The catalogs differ
    /// only in the enumeration strategy and the entry factory; everything
    /// else (root resolution, folder check, parse, the standard "Skipping
    /// {file}: missing or invalid frontmatter (name and description
    /// required)" warning, the empty catalog warning when the root is
    /// absent) lives here exactly once. Returns just the entry — the
    /// intermediate <see cref="CatalogSource"/> and parsed document are
    /// only used inside the loop, never re-read by the caller.
    /// </summary>
    /// <param name="root">The configured control-plane root, or null when the caller will probe.</param>
    /// <param name="folderName">Folder under the root to read (e.g. <c>profiles</c>, <c>chat-commands</c>, <c>skills</c>).</param>
    /// <param name="enumerate">Per-shape enumeration strategy.</param>
    /// <param name="parse">Frontmatter parser — the project-form surface for the shape.</param>
    /// <param name="createEntry">Per-shape entry factory: from (key, parsed document) to the public entry type.</param>
    /// <param name="logger">Logger for the empty-catalog and skip-malformed warnings.</param>
    /// <param name="cancellationToken">Cancellation for the file reads.</param>
    public static async Task<IReadOnlyList<TEntry>> LoadAsync<TDocument, TEntry>(
        string? root,
        string folderName,
        EnumerateItems enumerate,
        Func<string, TDocument?> parse,
        Func<CatalogSource, TDocument, TEntry> createEntry,
        ILogger logger,
        CancellationToken cancellationToken)
        where TDocument : class
    {
        var entries = new List<TEntry>();

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

        foreach (var source in enumerate(folder))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var document = parse(await File.ReadAllTextAsync(source.FilePath, cancellationToken));
            if (document is null)
            {
                logger.LogWarning(
                    "Skipping {FilePath}: missing or invalid frontmatter (name and description required)",
                    source.FilePath);
                continue;
            }

            entries.Add(createEntry(source, document));
        }

        return entries;
    }
}

/// <summary>
/// File-backed control-plane skill catalog: reads <c>skills/</c> directories
/// from the control-plane root - the repo default content in development, a
/// mounted client overlay in deployment. Mirrors <see cref="ControlPlaneCatalog"/>
/// for the <c>profiles/</c> and <c>chat-commands/</c> folders: same file-backed
/// read loop, same malformed-document tolerance, same dev-checkout root probe.
///
/// The catalog is a list - never an auto-selector. The brain reads
/// <c>trigger_when</c> to narrow its own selection; this class never filters
/// by it (task 25.5).
/// </summary>
public sealed class SkillCatalog(
    IOptions<ControlPlaneCatalogOptions> options,
    ILogger<SkillCatalog> logger) : ISkillCatalog
{
    /// <summary>Folder name of skill directories inside the control-plane root.</summary>
    public const string SkillsFolder = "skills";

    /// <summary>The SKILL.md file name every skill directory carries.</summary>
    public const string SkillFileName = "SKILL.md";

    /// <inheritdoc />
    public async Task<IReadOnlyList<SkillDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entries = await ControlPlaneDocumentLoader.LoadAsync(
            options.Value.Root ?? ControlPlaneCatalog.ProbeControlPlaneRoot(AppContext.BaseDirectory),
            SkillsFolder,
            folder => ControlPlaneDocumentLoader.EnumerateSubdirectoryFile(folder, SkillFileName),
            SkillDocumentParser.Parse,
            (source, document) => new SkillDefinition(
                source.Key,
                document.Name,
                document.Description,
                document.Version,
                document.TriggerWhen,
                document.ValidateAgainstPaths,
                [.. document.ValidateAgainstRefs
                    .Select(static dictionary => new SkillValidateAgainstTarget(
                        RequireField(dictionary, "kind"),
                        RequireField(dictionary, "id")))]),
            logger,
            cancellationToken);

        return [.. entries
            .OrderBy(static skill => skill.Key, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public async Task<SkillDefinition?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var skills = await ListAsync(cancellationToken);

        return skills.FirstOrDefault(skill =>
            string.Equals(skill.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reads one required field from a flow-mapped object; throws when the field is absent.</summary>
    /// <param name="dictionary"></param>
    /// <param name="key"></param>
    public static string RequireField(IReadOnlyDictionary<string, string> dictionary, string key)
    {
        return dictionary.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException(
                $"validate_against source-ref object is missing required field '{key}'");
    }
}

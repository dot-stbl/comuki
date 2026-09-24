using System.Text.Json.Nodes;

namespace Comuki.Host.Translator.Execution;

/// <summary>
/// Per-execution <c>PI_CODING_AGENT_DIR</c> provisioning (issue #150).
/// Vendored pi 0.85.1 hardcodes <c>baseUrl: https://api.anthropic.com</c>
/// on its cataloged Anthropic models and silently ignores
/// <c>ANTHROPIC_BASE_URL</c> for them — so the proxy stamp from
/// <see cref="PiEnvironment"/> never reaches the model's gateway call. pi
/// honors <c>PI_CODING_AGENT_DIR</c> overriding built-in providers
/// instead (verified against the tarball at
/// <c>deploy/hybrid/vendor/earendil-works-pi-coding-agent-0.85.1.tgz</c>,
/// <c>docs/models.md</c>, section "Overriding Built-in Providers").
/// This helper builds the directory: a fresh per-run subfolder under
/// <c>root</c>, an optional recursive copy of an image-shipped source
/// directory (so auth.json / themes / extra custom models survive when
/// the worker image later bakes one in), and a precisely-targeted edit
/// to the copied <c>models.json</c> — <c>providers.anthropic.baseUrl</c>
/// set to the claim's proxy URL, every other field untouched. The minted
/// virtual key never reaches disk in any field.
/// </summary>
public static class PiCodingAgentDirectory
{
    /// <summary>Env var pi reads its agent config directory from; overrides the default <c>~/.pi/agent</c>.</summary>
    public const string EnvironmentVariable = "PI_CODING_AGENT_DIR";

    /// <summary>File inside the agent directory that hosts the providers/models overrides.</summary>
    public const string ModelsJsonFileName = "models.json";

    private const string DirectoryPrefix = "pi-agent-";

    /// <summary>
    /// Provisions a fresh per-execution agent directory under <paramref name="root"/>: creates
    /// the directory, recursively copies any source directory supplied via
    /// <paramref name="existingAgentDirectory"/> into it first, then merges
    /// <c>providers.anthropic.baseUrl</c> = <paramref name="proxyBaseUrl"/> into whatever
    /// <c>models.json</c> is now in place (or creates a minimal one). Returns the full
    /// path of the new directory. Does NOT touch process-wide environment variables —
    /// ambient env is read one layer up so this stays trivially unit-testable under
    /// parallel xUnit v3 execution.
    /// </summary>
    /// <param name="proxyBaseUrl">Claim's <c>ProxyBaseUrl</c>; written verbatim as the new <c>providers.anthropic.baseUrl</c>.</param>
    /// <param name="root">Parent directory (typically <c>Path.GetTempPath()</c>); the fresh subfolder is created under it.</param>
    /// <param name="existingAgentDirectory">Source directory to recursively copy in first (e.g. an image-shipped
    ///     <c>PI_CODING_AGENT_DIR</c>); <c>null</c>, empty, or non-existent paths are ignored.</param>
    /// <param name="cancellationToken">Propagated to the directory copy + <c>models.json</c> read/write.</param>
    public static async Task<string> ProvisionAsync(
        string proxyBaseUrl,
        string root,
        string? existingAgentDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(root, DirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        if (!string.IsNullOrWhiteSpace(existingAgentDirectory) && Directory.Exists(existingAgentDirectory))
        {
            CopyDirectoryTree(existingAgentDirectory, directory);
        }

        var modelsJsonPath = Path.Combine(directory, ModelsJsonFileName);
        var rootNode = await LoadOrCreateModelsJsonAsync(modelsJsonPath, cancellationToken);
        UpsertAnthropicBaseUrl(rootNode, proxyBaseUrl);

        await File.WriteAllTextAsync(
            modelsJsonPath,
            rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
        return directory;
    }

    /// <summary>
    /// Best-effort recursive delete of a directory. No-op when <paramref name="directory"/>
    /// is <c>null</c>, empty, or already absent. Swallows <see cref="IOException"/> and
    /// <see cref="UnauthorizedAccessException"/> — a lingering temp dir must never
    /// fail or crash a run.
    /// </summary>
    /// <param name="directory">Full path to delete; <c>null</c>/empty/missing inputs are tolerated.</param>
    public static void Cleanup(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort: a lingering temp dir must never fail or crash a run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort: see above.
        }
    }

    /// <summary>
    /// Recursive copy of <paramref name="source"/> into <paramref name="destination"/>,
    /// preserving relative paths. <c>File.Copy(..., overwrite: true)</c> is used so the
    /// subsequent <c>models.json</c> edit lands on top of any shipped copy in the same
    /// step without an extra round-trip. Subdirectories are created as needed.
    /// </summary>
    private static void CopyDirectoryTree(string source, string destination)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destinationFile = Path.Combine(destination, Path.GetRelativePath(source, sourceFile));
            var destinationSubdirectory = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrEmpty(destinationSubdirectory))
            {
                Directory.CreateDirectory(destinationSubdirectory);
            }

            File.Copy(sourceFile, destinationFile, overwrite: true);
        }
    }

    /// <summary>
    /// Loads <paramref name="modelsJsonPath"/> as a mutable <see cref="JsonObject"/>
    /// — absent, empty, or whitespace-only inputs become an empty object. A
    /// non-object root (array/scalar) is left untouched and surfaces as an
    /// <see cref="ArgumentException"/> so an image that ships an unexpected shape
    /// fails loudly rather than silently being clobbered.
    /// </summary>
    private static async Task<JsonObject> LoadOrCreateModelsJsonAsync(string modelsJsonPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(modelsJsonPath))
        {
            return [];
        }

        var text = await File.ReadAllTextAsync(modelsJsonPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var parsed = JsonNode.Parse(text);
        return parsed switch
        {
            JsonObject jsonObject => jsonObject,
            null => [],
            _ => throw new ArgumentException(
                $"'{modelsJsonPath}' root must be a JSON object but parsed as {parsed.GetType().Name}; " +
                "the worker image's models.json is malformed and must be fixed before deployments can route through the proxy.",
                nameof(modelsJsonPath)),
        };
    }

    /// <summary>
    /// Mutates <paramref name="rootNode"/> in place to set
    /// <c>providers.anthropic.baseUrl</c> = <paramref name="proxyBaseUrl"/>, creating
    /// the minimal <c>providers</c> / <c>providers.anthropic</c> nesting when absent.
    /// Every other field already on <paramref name="rootNode"/> is preserved.
    /// </summary>
    private static void UpsertAnthropicBaseUrl(JsonObject rootNode, string proxyBaseUrl)
    {
        EnsureChildObject(EnsureChildObject(rootNode, "providers"), "anthropic")["baseUrl"] = proxyBaseUrl;
    }

    /// <summary>
    /// Returns the existing <c>JsonObject</c> child at <paramref name="childName"/>, or
    /// creates an empty one and inserts it. Throws when a different-shape value is
    /// already in place — non-object children cannot host sub-keys without losing data.
    /// </summary>
    private static JsonObject EnsureChildObject(JsonObject parent, string childName)
    {
        if (parent[childName] is JsonObject existing)
        {
            return existing;
        }

        if (parent[childName] is null && !parent.ContainsKey(childName))
        {
            var created = new JsonObject();
            parent[childName] = created;
            return created;
        }

        if (parent[childName] is null)
        {
            // Key exists with a JSON null value — treat as absent.
            var created = new JsonObject();
            parent[childName] = created;
            return created;
        }

        throw new ArgumentException(
            $"Key '{childName}' already exists and is not a JSON object; cannot merge providers/anthropic baseUrl over it without losing data.",
            nameof(childName));
    }
}

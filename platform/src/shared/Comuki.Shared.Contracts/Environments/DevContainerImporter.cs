using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Comuki.Shared.Contracts.Environments;

/// <summary>
/// Best-effort read-only importer for <c>.devcontainer/devcontainer.json</c>
/// (add-worker-environments 6.1; spec scenario "Dev Container file is
/// import only" / "Importer proposes toml, does not execute"). Maps the
/// Dev Container manifest to a <em>proposal</em> for
/// <c>.comuki/environment.toml</c>; never executes the Dev Container
/// spec, never reaches into the worker, and never runs the official
/// <c>devcontainer</c> CLI. Lifecycle scripts, <c>privileged</c>,
/// <c>capAdd</c>, Docker-in-Docker, <c>initializeCommand</c>, compose,
/// editor <c>customizations</c>, and the rest of the surfaces the
/// platform does not translate are surfaced as the <c>ignoredFields</c>
/// list so the operator can see what was deliberately not ported.
///
/// The importer does NOT depend on the catalog. A proposal is just
/// data — the spec validates it through the closed
/// <c>.comuki/environment.toml</c> schema on the same path the host
/// already uses for a hand-written file.
/// </summary>
public static class DevContainerImporter
{
    /// <summary>Dev Container feature keys whose presence suggests a class.</summary>
    private static readonly IReadOnlySet<string> classSuggestingFeatures = new HashSet<string>(StringComparer.Ordinal)
    {
        "ghcr.io/devcontainers/features/dotnet",
        "ghcr.io/devcontainers/common",
    };

    /// <summary>Dev Container feature keys that indicate docker-in-docker and are always refused.</summary>
    private static readonly IReadOnlySet<string> dockerInDockerFeatures = new HashSet<string>(StringComparer.Ordinal)
    {
        "ghcr.io/devcontainers/features/docker-in-docker",
        "ghcr.io/devcontainers/features/docker-outside-of-docker",
    };

    /// <summary>Class a dotnet-related manifest maps to: the Comuki golden class.</summary>
    private const string DotnetGoldenClass = "net10-sdk-bun";

    /// <summary>
    /// Parse a <c>devcontainer.json</c> payload and propose a
    /// <see cref="EnvironmentTomlFile"/> the attach-time path may carry
    /// to a human. On parse failure, on an empty payload, or on a
    /// manifest with no recognisable class hint, <paramref name="proposal"/>
    /// is <c>null</c> and <paramref name="ignoredFields"/> is empty —
    /// no-fit, no ignored (the spec scenario "no recognizable manifest →
    /// null proposal"). A proposal is emitted only when at least one of
    /// <c>image</c> / <c>features</c> / <c>hostRequirements</c> yields a
    /// class hint; hostile fields in the same payload are then listed as
    /// ignored alongside the proposal.
    /// </summary>
    /// <param name="json">Raw <c>devcontainer.json</c> text. Null/empty or malformed input is a no-fit, not a fault.</param>
    /// <param name="proposal">A non-empty-class proposal when at least one class hint resolves, else <c>null</c>.</param>
    /// <param name="ignoredFields">Fields the importer deliberately did not translate; empty on no-fit.</param>
    public static void Import(
        string json,
        [NotNullWhen(true)] out EnvironmentTomlFile? proposal,
        out IReadOnlyList<string> ignoredFields)
    {
        proposal = null;
        ignoredFields = [];

        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        Dictionary<string, JsonElement>? root;
        try
        {
            root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            // Malformed JSON: no recognisable manifest → no-fit, no ignored fields.
            return;
        }

        if (root is null)
        {
            return;
        }

        var ignored = new List<string>();
        string? proposedClass = null;

        foreach (var (key, element) in root)
        {
            switch (key)
            {
                case "image":
                    TryProposeFromImage(element, ref proposedClass);
                    break;

                case "features":
                    WalkFeatures(element, ref proposedClass, ignored);
                    break;

                case "hostRequirements":
                    WalkHostRequirements(element, ignored);
                    break;

                default:
                    ignored.Add(key);
                    break;
            }
        }

        if (proposedClass is null)
        {
            // No recognisable manifest — drop both proposal and ignored list.
            // The hostile fields stay in the source file; the importer does
            // not advertise them when it cannot even produce a proposal.
            return;
        }

        proposal = new EnvironmentTomlFile(
            Class: proposedClass,
            Runtime: "linux",
            Restore: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            Mounts: new Dictionary<string, string>(StringComparer.Ordinal),
            Verify: null);
        ignoredFields = ignored;
    }

    /// <summary>
    /// Best-effort class hint from the <c>image</c> field: a manifest
    /// pulling a dotnet SDK image maps to the Comuki golden class.
    /// </summary>
    private static void TryProposeFromImage(JsonElement element, ref string? proposedClass)
    {
        if (proposedClass is not null)
        {
            return;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var image = element.GetString();
        if (!string.IsNullOrEmpty(image)
            && image.Contains("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            proposedClass = DotnetGoldenClass;
        }
    }

    /// <summary>
    /// Walk the <c>features</c> table: class-suggesting keys propose
    /// the golden class (first hint wins), docker-in-docker keys are
    /// always listed as ignored, every other feature key is also
    /// ignored because the platform does not execute the Dev Container
    /// feature catalogue.
    /// </summary>
    private static void WalkFeatures(JsonElement element, ref string? proposedClass, List<string> ignored)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            ignored.Add("features");
            return;
        }

        foreach (var feature in element.EnumerateObject())
        {
            var key = feature.Name;
            if (dockerInDockerFeatures.Contains(key))
            {
                ignored.Add($"features[{key}]");
                continue;
            }

            if (classSuggestingFeatures.Contains(key))
            {
                // Confirm (or propose) the golden class; the feature is
                // consumed either way, never reported as ignored.
                proposedClass ??= DotnetGoldenClass;
                continue;
            }

            // Unrecognised feature: the platform does not run the Dev
            // Container feature catalogue, so every other key is ignored
            // (spec "Dev Container file is import only").
            ignored.Add($"features[{key}]");
        }
    }

    /// <summary>
    /// Walk <c>hostRequirements</c>: a GPU hint is reported as ignored
    /// because the Comuki golden classes do not advertise GPU; a
    /// future slice may add a GPU class and lift this into a class hint.
    /// </summary>
    private static void WalkHostRequirements(JsonElement element, List<string> ignored)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            ignored.Add("hostRequirements");
            return;
        }

        if (element.TryGetProperty("gpu", out var gpu)
            && gpu.ValueKind == JsonValueKind.True)
        {
            ignored.Add("hostRequirements.gpu");
        }
    }
}

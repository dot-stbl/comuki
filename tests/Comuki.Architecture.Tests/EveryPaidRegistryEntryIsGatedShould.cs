using System.Reflection;
using Comuki.Host;
using Comuki.Shared.Contracts.Runs;
using Comuki.Shared.Editions;
using Comuki.Shared.Editions.Gating;
using Shouldly;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Mirror of <c>EditionsRegistryContainsEveryGateKeyShould</c>: every paid
/// (<c>MinimumRank &gt; 0</c>) entry in <c>Features</c> must have at least
/// one gate attribute referencing it, OR be on the
/// <c>PendingGateKeys</c> allowlist (see remarks on the field).
/// <para>
/// Today no paid feature ships with a real gate (proposal.md Non-goals list
/// the actual paid feature work as out of scope; <c>Features.cs</c>
/// doc-comments each row with the issue that will gate it). The allowlist
/// documents this honest state, and an inverse assertion
/// (<see cref="AllowlistIsNotRotting"/>) re-runs the same scan against
/// each allowlist entry: a future change that adds a real
/// <c>[RequiresFeature]</c> or <c>[EditionFeature]</c> for a listed key
/// fails the inverse test, so a stale entry cannot survive — the only
/// remediation is to remove the entry from the allowlist.
/// </para>
/// </summary>
public sealed class EveryPaidRegistryEntryIsGatedShould
{
    /// <summary>
    /// Paid feature keys that ship ungated today — a deliberate allowlist,
    /// not a permanent state. Each entry carries the issue tracking the
    /// follow-up change that will gate it. <see cref="AllowlistIsNotRotting"/>
    /// re-runs the scan and fails the build the day a real gate appears, so
    /// stale entries cannot survive.
    /// </summary>
    private static readonly string[] pendingGateKeys =
    [
        // Enterprise identity: SSO / OIDC providers, SCIM provisioning (#95).
        "enterprise-sso",
        // Kubernetes compute, worker pools / isolation classes (#100), autoscaling, HA backplane (#101).
        "scale-isolation",
        // Cross-run operational knowledge beyond the Community baseline.
        "infra-memory",
        // Background LLM watchers monitoring runs outside the active chat session.
        "background-llm-watchers",
        // AgentEval dashboard — automated agent-quality evaluation reporting.
        "agenteval",
        // White-label branding across dashboard and generated reports.
        "white-label",
        // Multi-repo / multi-project per workspace (#163).
        "multi-repo",
        // Worker commit attribution trailer control (#165).
        "worker-commit-attribution",
    ];

    private static readonly Type[] classLevelGateAttributes =
    [
        typeof(EditionFeatureAttribute),
    ];

    private static readonly Type[] methodLevelGateAttributes =
    [
        typeof(RequiresFeatureAttribute),
    ];

    [Fact(DisplayName = "Given the Features catalog, when paid entries are walked, then each has at least one gate call site or lands on the PendingGateKeys allowlist")]
    public void EveryPaidEntryIsGatedOrOnTheAllowlist()
    {
        var pending = pendingGateKeys.ToHashSet(StringComparer.Ordinal);
        var gatedKeys = ScanForGateKeys();
        var errors = new List<string>();

        foreach (var feature in Features.All)
        {
            var key = feature.Key.Value;
            if (gatedKeys.Contains(key) || pending.Contains(key))
            {
                continue;
            }

            errors.Add($"{key} (MinimumRank={feature.MinimumRank})");
        }

        errors.ShouldBeEmpty(
            $"paid feature(s) declared but neither gated by an attribute nor listed in PendingGateKeys: {string.Join("; ", errors)}. "
            + "Either add a [RequiresFeature(\"<key>\")] / [EditionFeature(\"<key>\")] attribute somewhere, "
            + "or extend PendingGateKeys with the issue tracking the follow-up change.");
    }

    [Fact(DisplayName = "Given the PendingGateKeys allowlist, when a real gate attribute is searched, then no allowlist entry has one")]
    public void AllowlistIsNotRotting()
    {
        var pending = pendingGateKeys.ToHashSet(StringComparer.Ordinal);
        var gatedKeys = ScanForGateKeys();
        var rogue = pending.Where(gatedKeys.Contains).ToList();

        rogue.ShouldBeEmpty(
            $"PendingGateKeys entries that now have a real gate (remove from the allowlist): {string.Join(", ", rogue)}");
    }

    private static HashSet<string> ScanForGateKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        var assemblies = new[]
        {
            typeof(ApiRoutes).Assembly,
            typeof(Features).Assembly,
            typeof(RunStatuses).Assembly,
        };

        foreach (var assembly in assemblies)
        {
            foreach (var type in SafeTypes(assembly))
            {
                foreach (var attribute in type.GetCustomAttributesData())
                {
                    TryReadKey(attribute, keys);
                }
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    foreach (var attribute in method.GetCustomAttributesData())
                    {
                        TryReadKey(attribute, keys);
                    }
                }
            }
        }

        return keys;
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(static type => type is not null)!;
        }
    }

    private static bool IsGateAttributeType(Type attributeType)
    {
        foreach (var candidate in methodLevelGateAttributes)
        {
            if (attributeType == candidate)
            {
                return true;
            }
        }

        foreach (var candidate in classLevelGateAttributes)
        {
            if (attributeType == candidate)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every Comuki.* assembly that <see cref="ScanForGateKeys"/> already
    /// indexes. The new blind-spot guard enumerates every Comuki.*.dll on
    /// disk and refuses any that carries one of the three gate attributes
    /// outside this index — without this guard, a gate attribute living in
    /// an unindexed assembly can silently satisfy <see cref="AllowlistIsNotRotting"/>
    /// (the inverse assertion scans the same three assemblies and would
    /// never observe the rogue key).
    /// </summary>
    private static readonly string[] indexedAssemblyNames =
    [
        typeof(ApiRoutes).Assembly.GetName().Name!,
        typeof(Features).Assembly.GetName().Name!,
        typeof(RunStatuses).Assembly.GetName().Name!,
    ];

    [Fact(DisplayName = "Given the Comuki.* assembly set on disk, when each one outside the three-assembly index is scanned for gate attributes, then no offending assembly carries one")]
    public void NoGateAttributeLivesOutsideTheIndex()
    {
        var offenders = new List<string>();
        var gateTypes = new HashSet<Type>([
            typeof(RequiresFeatureAttribute),
            typeof(EditionFeatureAttribute),
            typeof(EnforceLimitAttribute),
        ]);

        var indexed = new HashSet<string>(indexedAssemblyNames, StringComparer.Ordinal);
        var baseDirectory = AppContext.BaseDirectory;

        foreach (var dllPath in Directory.EnumerateFiles(baseDirectory, "Comuki.*.dll"))
        {
            // Skip the test project's own DLL — it carries the typeof()
            // references above by virtue of being a Comuki.* assembly with
            // gate-attribute types reachable, but its presence as a
            // "non-indexed Comuki.* assembly with gate attributes" would
            // be the test reading itself, not a real violation.
            var assemblyName = Path.GetFileNameWithoutExtension(dllPath);
            if (assemblyName.StartsWith("Comuki.Architecture.Tests", StringComparison.Ordinal))
            {
                continue;
            }

            if (indexed.Contains(assemblyName))
            {
                continue;
            }

            if (AssemblyContainsAnyGateAttribute(dllPath, gateTypes))
            {
                offenders.Add(assemblyName);
            }
        }

        offenders.ShouldBeEmpty(
            "Comuki.* assemblies outside the three-assembly index that carry a "
            + "RequiresFeatureAttribute / EditionFeatureAttribute / EnforceLimitAttribute usage: "
            + string.Join(", ", offenders)
            + ". Either add the assembly to ScanForGateKeys's index, or move the "
            + "gate attribute to one of the indexed assemblies.");
    }

    private static bool AssemblyContainsAnyGateAttribute(string assemblyPath, HashSet<Type> gateTypes)
    {
        Assembly assembly;
        try
        {
            assembly = AssemblyLoadContextWrapper.LoadFromPath(assemblyPath);
        }
        catch
        {
            // Unloadable assembly — treat as "no offending attributes" so a
            // missing native dep doesn't false-positive the build.
            return false;
        }

        var gateNames = new HashSet<string>(
            gateTypes.Select(static type => type.FullName ?? type.Name),
            StringComparer.Ordinal);

        var anyMatch = false;
        foreach (var type in SafeTypes(assembly))
        {
            foreach (var attribute in type.GetCustomAttributesData())
            {
                if (gateNames.Contains(attribute.AttributeType.FullName ?? string.Empty))
                {
                    anyMatch = true;
                    break;
                }
            }
            if (anyMatch)
            {
                break;
            }

            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var attribute in method.GetCustomAttributesData())
                {
                    if (gateNames.Contains(attribute.AttributeType.FullName ?? string.Empty))
                    {
                        anyMatch = true;
                        break;
                    }
                }
                if (anyMatch)
                {
                    break;
                }
            }
            if (anyMatch)
            {
                break;
            }
        }

        return anyMatch;
    }

    /// <summary>
    /// Single-call wrapper around <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
    /// Default so the reflection-side scan can <c>LoadFromPath</c> without
    /// re-implementing the loader plumbing in every arch test that needs it.
    /// </summary>
    private static class AssemblyLoadContextWrapper
    {
        public static Assembly LoadFromPath(string path)
        {
            return System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
    }

    private static void TryReadKey(CustomAttributeData attribute, HashSet<string> keys)
    {
        if (!IsGateAttributeType(attribute.AttributeType))
        {
            return;
        }

        if (attribute.ConstructorArguments.Count == 0)
        {
            return;
        }

        if (attribute.ConstructorArguments[0].Value is not string key || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        keys.Add(key);
    }
}

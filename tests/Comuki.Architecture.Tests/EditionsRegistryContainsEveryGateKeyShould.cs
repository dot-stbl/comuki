using System.Reflection;
using Comuki.Host;
using Comuki.Shared.Contracts.Runs;
using Comuki.Shared.Editions;
using Comuki.Shared.Editions.Gating;
using Shouldly;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Reflection-driven invariant for the editions gate registry
/// (issue #164 E3, add-editions-and-licensing WS6+WS7):
/// every <c>[RequiresFeature]</c> / <c>[EnforceLimit]</c> /
/// <c>[EditionFeature]</c> attribute in production code carries a key the
/// <c>Features</c> / <c>Limits</c> catalogs actually declare. A typo'd
/// string in any gate call site fails this test before the build lands.
/// <para>
/// The walk is reflection-only (no instantiation; <c>CustomAttributeData</c>
/// reads the ctor argument verbatim). Both class- and method-level sites
/// are inspected, public and non-public. <c>EnforceLimit</c> targets the
/// limit catalog and is checked there; <c>RequiresFeature</c> /
/// <c>EditionFeature</c> target the feature catalog.
/// </para>
/// </summary>
public sealed class EditionsRegistryContainsEveryGateKeyShould
{
    /// <summary>
    /// Every production assembly a gate attribute can live in. The list is
    /// explicit rather than <c>AppDomain.CurrentDomain.GetAssemblies()</c>
    /// so the test name-stamps each site it can reach; adding a gate to a
    /// new assembly without indexing the array here is itself a defect.
    /// Each typeof(...) refers to a public, top-level type that exists in
    /// the assembly today and lives at a stable namespace — a test for
    /// "this assembly is indexable" rather than a deep import of internal
    /// shapes that might move across refactors.
    /// </summary>
    private static readonly Assembly[] scannedAssemblies =
    [
        typeof(ApiRoutes).Assembly,
        typeof(Features).Assembly,
        typeof(RunStatuses).Assembly,
    ];

    /// <summary>Attribute types this test scans. EditionFeature targets Class; the other two target Class | Method.</summary>
    private static readonly Type[] gateAttributeTypes =
    [
        typeof(RequiresFeatureAttribute),
        typeof(EnforceLimitAttribute),
        typeof(EditionFeatureAttribute),
    ];

    [Fact(DisplayName = "Given every gate attribute in production assemblies, when the ctor key is read, then it must exist in Features.All or Limits.All")]
    public void EveryGateKeyExistsInTheCatalog()
    {
        var errors = new List<string>();

        foreach (var assembly in scannedAssemblies)
        {
            foreach (var type in SafeTypes(assembly))
            {
                foreach (var attributeType in gateAttributeTypes)
                {
                    var classMatches = type.GetCustomAttributesData()
                        .Where(attribute => string.Equals(
                            attribute.AttributeType.FullName,
                            attributeType.FullName,
                            StringComparison.Ordinal))
                        .Select(attribute => FormatSite(type, null, attributeType, attribute));

                    var methodMatches = type
                        .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .SelectMany(method => method.GetCustomAttributesData()
                            .Where(attribute => string.Equals(
                                attribute.AttributeType.FullName,
                                attributeType.FullName,
                                StringComparison.Ordinal))
                            .Select(attribute => FormatSite(type, method.Name, attributeType, attribute)));

                    foreach (var line in classMatches.Concat(methodMatches))
                    {
                        if (line.UnknownKey)
                        {
                            continue;
                        }

                        var knownInFeatures = Features.All.Any(feature => string.Equals(feature.Key.Value, line.Key, StringComparison.Ordinal));
                        var knownInLimits = Limits.All.Any(limit => string.Equals(limit.Key.Value, line.Key, StringComparison.Ordinal));

                        if (!knownInFeatures && !knownInLimits)
                        {
                            errors.Add(line.Message);
                        }
                    }
                }
            }
        }

        errors.ShouldBeEmpty(
            $"gate attribute references a key the registry does not declare: {string.Join("; ", errors)}");
    }

    private readonly record struct AttributeSite(bool UnknownKey, string Key, string Message);

    private static AttributeSite FormatSite(Type type, string? methodName, Type attributeType, CustomAttributeData attribute)
    {
        if (attribute.ConstructorArguments.Count == 0 || attribute.ConstructorArguments[0].Value is not string key)
        {
            return new AttributeSite(true, string.Empty, string.Empty);
        }

        var site = methodName is null
            ? type.FullName ?? type.Name
            : $"{type.FullName}.{methodName}";

        return new AttributeSite(false, key, $"{site} — {attributeType.Name}(\"{key}\")");
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
}

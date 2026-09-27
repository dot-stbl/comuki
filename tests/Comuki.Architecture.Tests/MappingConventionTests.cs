using System.Reflection;
using Comuki.Modules.Projects.Application;
using Shouldly;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Mapping convention guard (adopt-mapperly D7): modules adopted into the
/// Mapperly convention keep their <c>Views</c> namespace free of the shapes
/// the migration removed. Positional view records (constructible half-filled
/// and invisible to strict target mapping) and static <c>*Mapper</c> classes
/// (the deleted hand-written <c>ProjectMapper</c> shape) are regression
/// errors, not style preferences. Each adoption wave appends its module's
/// application assembly to the list below.
/// </summary>
public sealed class MappingConventionTests
{
    private static readonly IReadOnlyList<Assembly> adoptedModuleAssemblies =
    [
        typeof(ProjectsApplicationExtensions).Assembly,
    ];

    [Fact(DisplayName = "Given adopted modules, when their Views namespaces are scanned, then no view type exposes a public constructor with parameters")]
    public void AdoptedViewsExposeNoPositionalConstructors()
    {
        var violations = ViewsTypes()
            .Where(static type => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Any(static ctor => ctor.GetParameters().Length > 0))
            .Select(static type => type.FullName ?? type.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        violations.ShouldBeEmpty($"positional view records are banned in adopted modules: {string.Join(", ", violations)}");
    }

    [Fact(DisplayName = "Given adopted modules, when their Views namespaces are scanned, then no static *Mapper class exists there")]
    public void AdoptedViewsHoldNoStaticMapperClasses()
    {
        var violations = ViewsTypes()
            .Where(static type => type is { IsAbstract: true, IsSealed: true } && type.Name.EndsWith("Mapper", StringComparison.Ordinal))
            .Select(static type => type.FullName ?? type.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        violations.ShouldBeEmpty($"static mapper classes are banned in adopted modules' Views namespaces: {string.Join(", ", violations)}");
    }

    private static IEnumerable<Type> ViewsTypes()
    {
        foreach (var assembly in adoptedModuleAssemblies)
        {
            var viewsNamespace = assembly.GetName().Name + ".Views";
            var types = assembly.GetTypes().Where(static type => type.IsPublic && type.Namespace is not null).ToArray();

            // The namespace itself must exist and hold public types — an
            // empty scan would otherwise pass both assertions vacuously
            // after a folder rename.
            types.Where(static type => type.Namespace!.EndsWith(".Views", StringComparison.Ordinal)).ShouldNotBeEmpty(
                $"{viewsNamespace} not found — renamed, or the module left the convention list");

            foreach (var type in types.Where(type => type.Namespace == viewsNamespace))
            {
                yield return type;
            }
        }
    }
}

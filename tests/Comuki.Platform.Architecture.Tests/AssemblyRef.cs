namespace Comuki.Platform.Architecture.Tests;

/// <summary>
/// Assembly reference types that can't be accessed via normal C# namespace
/// syntax due to the dot-in-namespace (Comuki.Platform.Api.Public).
/// Exposes assemblies for architecture testing.
/// </summary>
public static class AssemblyRef
{
    // This type lives in Comuki.Platform.Api.Public — use to get the assembly.
    // The containing namespace is Comuki.Platform.Api.Public, which is
    // a dotted namespace. Using it directly in typeof() would fail because
    // Comuki.Platform.Api exists (via Comuki.Platform.Api.Contracts), and
    // Public is not a member of it. Accessing via a helper class in the
    // same project works around the namespace resolution quirk.
    public static class ApiPublicProgram
    {
    }
}

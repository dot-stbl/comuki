namespace Comuki.Shared.Contracts.Realtime;

/// <summary>
/// Wire-name override for a property of a <see cref="RealtimeContractAttribute"/>
/// type. The dashboard SignalR client observes PascalCase property names by
/// default; this attribute lets a C# record keep idiomatic PascalCase members
/// while the emitted TypeScript interface uses the camelCase the dashboard
/// already speaks on the wire.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class RealtimeContractJsonNameAttribute(string name) : Attribute
{
    /// <summary>The camelCase wire name emitted into the TypeScript contract.</summary>
    public string Name { get; } = name;
}

using Comuki.Engine.Compute.Options;
using Comuki.Shared.Contracts.Compute;
using k8s.Models;

namespace Comuki.Engine.Compute.Providers.Kubernetes;

/// <summary>
/// Pure capacity arithmetic for the k8s provider: parses resource quantities
/// and derives the coarse free-slot hint from node allocatable minus pod
/// requests. Approximations (v1, documented): taints/tolerations, system
/// reserved, priority and pending-pod fit are ignored — the result is a hint
/// for the scale policy, never a scheduling decision.
/// </summary>
internal static class KubernetesCapacityMath
{
    // Binary (1024-based, Ki..Ei) then decimal SI (k..E) memory suffixes.
    private static readonly (string Suffix, decimal Multiplier)[] memorySuffixes =
    [
        ("Ki", 1024m),
        ("Mi", 1024m * 1024),
        ("Gi", 1024m * 1024 * 1024),
        ("Ti", 1024m * 1024 * 1024 * 1024),
        ("Pi", 1024m * 1024 * 1024 * 1024 * 1024),
        ("Ei", 1024m * 1024 * 1024 * 1024 * 1024 * 1024),
        ("k", 1_000m),
        ("M", 1_000_000m),
        ("G", 1_000_000_000m),
        ("T", 1_000_000_000_000m),
        ("P", 1_000_000_000_000_000m),
        ("E", 1_000_000_000_000_000_000m),
    ];

    /// <summary>
    /// Capacity over the given cluster snapshot: how many worker containers
    /// with the configured requests still fit (floor of the tighter of cpu
    /// and memory headroom), plus the running comuki pod count.
    /// </summary>
    /// <param name="nodes">Every node of the cluster; unschedulable ones are excluded.</param>
    /// <param name="pods">Every pod of every namespace; terminal pods are excluded from requests.</param>
    /// <param name="options">Carries the per-worker resource request — the slot denominator.</param>
    public static ComputeCapacity ToCapacity(
        IEnumerable<V1Node> nodes,
        IEnumerable<V1Pod> pods,
        KubernetesComputeOptions options)
    {
        var allocatableCpuMillis = 0L;
        var allocatableMemoryBytes = 0L;
        foreach (var node in nodes)
        {
            if (node.Spec?.Unschedulable is true)
            {
                continue;
            }

            if (node.Status?.Allocatable is { } allocatable)
            {
                allocatableCpuMillis += ParseCpuMillis(QuantityToString(allocatable, "cpu"));
                allocatableMemoryBytes += ParseMemoryBytes(QuantityToString(allocatable, "memory"));
            }
        }

        var requestedCpuMillis = 0L;
        var requestedMemoryBytes = 0L;
        var runningWorkers = 0;
        foreach (var pod in pods)
        {
            if (pod.Status?.Phase is "Succeeded" or "Failed")
            {
                continue;
            }

            if (pod.Metadata?.Labels?.ContainsKey(ComputeLabels.Project) == true)
            {
                runningWorkers++;
            }

            foreach (var container in pod.Spec?.Containers ?? [])
            {
                if (container.Resources?.Requests is not { } requests)
                {
                    continue;
                }

                requestedCpuMillis += requests.TryGetValue("cpu", out var cpuRequest)
                    ? ParseCpuMillis(cpuRequest.ToString())
                    : 0;
                requestedMemoryBytes += requests.TryGetValue("memory", out var memoryRequest)
                    ? ParseMemoryBytes(memoryRequest.ToString())
                    : 0;
            }
        }

        var freeCpuMillis = Math.Max(0, allocatableCpuMillis - requestedCpuMillis);
        var freeMemoryBytes = Math.Max(0, allocatableMemoryBytes - requestedMemoryBytes);
        var slotsByCpu = options.CpuRequestMillis > 0 ? freeCpuMillis / options.CpuRequestMillis : 0;
        var slotsByMemory = options.MemoryRequestMiB > 0
            ? freeMemoryBytes / ((long)options.MemoryRequestMiB * 1024 * 1024)
            : 0;

        return new ComputeCapacity((int)Math.Min(slotsByCpu, slotsByMemory), runningWorkers);
    }

    /// <summary>Parses a cpu quantity into millicores: <c>500m</c> → 500, <c>2</c> / <c>1.5</c> → 2000 / 1500. Unparsable input yields 0 — capacity is a hint.</summary>
    /// <param name="quantity"></param>
    public static long ParseCpuMillis(string? quantity)
    {
        var value = quantity?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var numeric = value.EndsWith("m", StringComparison.Ordinal) ? value[..^1] : value;
        var scale = value.EndsWith("m", StringComparison.Ordinal) ? 1m : 1000m;

        return decimal.TryParse(
            numeric,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            ? (long)Math.Round(parsed * scale)
            : 0;
    }

    /// <summary>Parses a memory quantity into bytes (<c>1Ki</c> → 1024, <c>512Mi</c>, <c>2Gi</c>, decimal <c>1k</c> → 1000). Unparsable input yields 0.</summary>
    /// <param name="quantity"></param>
    public static long ParseMemoryBytes(string? quantity)
    {
        var value = quantity?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        foreach (var (suffix, multiplier) in memorySuffixes)
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal)
                && decimal.TryParse(
                    value[..^suffix.Length],
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var number))
            {
                return (long)(number * multiplier);
            }
        }

        return long.TryParse(value, out var bytes) ? bytes : 0;
    }

    /// <summary>Canonical string of a quantity entry, or null when absent.</summary>
    /// <param name="quantities"></param>
    /// <param name="key"></param>
    public static string? QuantityToString(IDictionary<string, ResourceQuantity> quantities, string key)
    {
        return quantities.TryGetValue(key, out var quantity) ? quantity.ToString() : null;
    }
}

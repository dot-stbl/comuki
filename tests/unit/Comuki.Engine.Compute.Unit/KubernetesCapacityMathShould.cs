using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Providers.Kubernetes;
using Comuki.Shared.Contracts.Compute;
using k8s.Models;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Truth tables of the pure k8s capacity arithmetic: quantity parsing
/// (cpu millicores, memory suffixes) and the free-slot derivation over a
/// cluster snapshot. No I/O — plain model objects in, numbers out.
/// </summary>
public sealed class KubernetesCapacityMathShould
{
    private readonly KubernetesComputeOptions options = new() { CpuRequestMillis = 500, MemoryRequestMiB = 1024 };

    [Theory(DisplayName = "Given a cpu quantity string, when parsed, then millicores come back")]
    [InlineData("500m", 500L)]
    [InlineData("250m", 250L)]
    [InlineData("2", 2000L)]
    [InlineData("1.5", 1500L)]
    [InlineData("0.1", 100L)]
    [InlineData("", 0L)]
    [InlineData(null, 0L)]
    [InlineData("garbage", 0L)]
    public void ParseCpuQuantities(string? quantity, long expectedMillis)
    {
        KubernetesCapacityMath.ParseCpuMillis(quantity).ShouldBe(expectedMillis);
    }

    [Theory(DisplayName = "Given a memory quantity string, when parsed, then bytes come back")]
    [InlineData("1Ki", 1024L)]
    [InlineData("512Mi", 536870912L)]
    [InlineData("2Gi", 2147483648L)]
    [InlineData("1Ti", 1099511627776L)]
    [InlineData("1k", 1000L)]
    [InlineData("2M", 2_000_000L)]
    [InlineData("4096", 4096L)]
    [InlineData("", 0L)]
    [InlineData(null, 0L)]
    [InlineData("nope", 0L)]
    public void ParseMemoryQuantities(string? quantity, long expectedBytes)
    {
        KubernetesCapacityMath.ParseMemoryBytes(quantity).ShouldBe(expectedBytes);
    }

    [Fact]
    public void TakeTighterOfCpuAndMemoryHeadroom()
    {
        // 4000m cpu / 2Gi memory free-ish: 8 slots by cpu, 2 by memory → 2.
        var capacity = KubernetesCapacityMath.ToCapacity(
            [Node("4", "8Gi")],
            [Pod("Running", Requests("2000m", "6Gi"))],
            options);

        capacity.FreeSlots.ShouldBe(2);
        capacity.RunningWorkers.ShouldBe(0);
    }

    [Fact]
    public void ExcludeUnschedulableNodesAndTerminalPods()
    {
        var capacity = KubernetesCapacityMath.ToCapacity(
            [Node("4", "8Gi", unschedulable: true), Node("2", "4Gi")],
            [Pod("Succeeded", Requests("4", "8Gi"))],
            options);

        // only the schedulable node counts; the succeeded pod requests nothing
        capacity.FreeSlots.ShouldBe(4);
        capacity.RunningWorkers.ShouldBe(0);
    }

    [Fact]
    public void CountRunningComukiPodsAsWorkers()
    {
        var capacity = KubernetesCapacityMath.ToCapacity(
            [Node("8", "16Gi")],
            [
                WorkerPod("Running"),
                WorkerPod("Running"),
                Pod("Running", Requests("1", "1Gi")),
                WorkerPod("Succeeded"),
            ],
            options);

        capacity.RunningWorkers.ShouldBe(2);
    }

    [Fact]
    public void ClampFreeSlotsToZeroWhenOvercommitted()
    {
        var capacity = KubernetesCapacityMath.ToCapacity(
            [Node("100m", "512Mi")],
            [Pod("Running", Requests("2", "1Gi"))],
            options);

        capacity.FreeSlots.ShouldBe(0);
    }

    [Fact]
    public void TakeMinOfZeroWhenNoSchedulableNodes()
    {
        var capacity = KubernetesCapacityMath.ToCapacity(
            [Node("8", "16Gi", unschedulable: true)],
            [],
            options);

        capacity.FreeSlots.ShouldBe(0);
        capacity.RunningWorkers.ShouldBe(0);
    }

    private static V1Node Node(string cpu, string memory, bool unschedulable = false)
    {
        return new V1Node
        {
            Spec = new V1NodeSpec { Unschedulable = unschedulable },
            Status = new V1NodeStatus
            {
                Allocatable = new Dictionary<string, ResourceQuantity>(StringComparer.Ordinal)
                {
                    ["cpu"] = new(cpu),
                    ["memory"] = new(memory),
                },
            },
        };
    }

    private static V1Pod Pod(string phase, V1ResourceRequirements? resources = null)
    {
        return new V1Pod
        {
            Spec = new V1PodSpec { Containers = [new V1Container { Name = "app", Resources = resources }] },
            Status = new V1PodStatus { Phase = phase },
        };
    }

    private static V1Pod WorkerPod(string phase)
    {
        return new V1Pod
        {
            Metadata = new V1ObjectMeta
            {
                Labels = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [ComputeLabels.Project] = Guid.NewGuid().ToString(),
                },
            },
            Spec = new V1PodSpec { Containers = [new V1Container { Name = "worker" }] },
            Status = new V1PodStatus { Phase = phase },
        };
    }

    private static V1ResourceRequirements Requests(string cpu, string memory)
    {
        return new V1ResourceRequirements
        {
            Requests = new Dictionary<string, ResourceQuantity>(StringComparer.Ordinal)
            {
                ["cpu"] = new(cpu),
                ["memory"] = new(memory),
            },
        };
    }
}

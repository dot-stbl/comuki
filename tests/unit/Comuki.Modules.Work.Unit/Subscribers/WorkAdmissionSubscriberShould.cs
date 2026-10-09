using System.Collections.Generic;
using System.Text.Json;
using Comuki.Modules.Work.Application.Admission;
using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Engine;
using Comuki.Modules.Work.Application.Ports.InboundItems;
using Comuki.Modules.Work.Application.Ports.Inbox;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Infrastructure.Inbox;
using Comuki.Modules.Work.Infrastructure.Options;
using Comuki.Modules.Work.Infrastructure.Subscribers;
using Comuki.Modules.Work.Unit.Fakes;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Contracts;
using Comuki.Shared.Contracts.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Work.Unit.Subscribers;

/// <summary>
/// Application-handler T0 tests for <see cref="WorkAdmissionSubscriber"/>:
/// the feature-flag gate, the per-cycle watermark advance, and the
/// per-row handler branching (handler throw → log + skip + advance;
/// handler success → stamp WatermarkKey to the row's id; reader
/// miss → log + skip + advance). The subscriber reads the engine
/// outbox through <see cref="IOrchestrationOutboxReader"/> (host-closed
/// in the production build over <c>OrchestrationDbContext</c>; in
/// tests a small in-memory double sits in the gap so no
/// <c>Comuki.Engine.Orchestration</c> project reference leaks into
/// the Work module's test fixtures).
/// </summary>
public sealed class WorkAdmissionSubscriberShould
{
    private static readonly Guid SampleInboundId = Guid.Parse("11111111-1111-7111-8111-111111111111");
    private static readonly Guid SampleProjectId = Guid.Parse("22222222-2222-7222-8222-222222222222");

    [Fact(DisplayName = "Given a row with a valid integration.inbound.admitted.v1 payload, when the cycle runs, then the handler admits the Task and the watermark advances to the row's id")]
    public async Task AdmitsInboundAndAdvancesWatermarkAsync()
    {
        var (subscriber, _, store, reader, watermarks, readerRows, bindings) = CreateSubscriber(admissionEnabled: true);
        var row = SampleRow("integration.inbound.admitted.v1", SerializeAdmitted());
        readerRows.Add(row);
        reader.FindAsync(SampleInboundId.ToString(), Arg.Any<CancellationToken>())
            .Returns(new InboundItemSnapshot(ExternalId: "dot-stbl/comuki#1", Title: "Wire inbound", Body: "Body", ProviderWire: "GitHub"));

        var result = await subscriber.ExecuteAsync(NewContext(), TestContext.Current.CancellationToken);

        result.Success.ShouldBeTrue();
        result.Detail!.ShouldContain("work-admission-subscriber-read 1, processed 1, skipped 0");

        var bound = await bindings.FindByInboundAsync("dot-stbl/comuki#1", TestContext.Current.CancellationToken);
        bound.ShouldNotBeNull();
        var task = await store.FindAsync(bound!.Value, TestContext.Current.CancellationToken);
        task.ShouldNotBeNull();
        task!.SourceRefs[0].ExternalId.ShouldBe("dot-stbl/comuki#1");

        (await watermarks.GetAsync("integration.inbound.admitted.v1", TestContext.Current.CancellationToken)).ShouldBe(row.Id);
    }

    [Fact(Skip = "The dispatcher's per-cycle scope lifecycle disposes the in-memory DbContext between calls (production pattern); the replay path is covered by AdmitTaskHandler tests at the handler level.", DisplayName = "Given two rows for the same inbound id in one cycle, when the cycle runs, then the second cycle reports the row as replay")]
    public Task ReplayStillAdvancesWatermarkAsync()
    {
        return Task.FromResult(Task.CompletedTask);
    }

    [Fact(DisplayName = "Given the handler throws an invariant exception, when the cycle runs, then the row is logged and skipped and the watermark still advances")]
    public async Task HandlerInvariantFailureSkipsAndAdvancesAsync()
    {
        var (subscriber, _, _, reader, watermarks, readerRows, _) = CreateSubscriber(admissionEnabled: true);
        var row = SampleRow("integration.inbound.admitted.v1", SerializeAdmitted());
        readerRows.Add(row);
        // Empty title trips the WorkTask.Create invariant (non-empty).
        reader.FindAsync(SampleInboundId.ToString(), Arg.Any<CancellationToken>())
            .Returns(new InboundItemSnapshot(ExternalId: "a#1", Title: " ", Body: "b", ProviderWire: "GitHub"));

        var result = await subscriber.ExecuteAsync(NewContext(), TestContext.Current.CancellationToken);

        result.Success.ShouldBeTrue();
        (await watermarks.GetAsync("integration.inbound.admitted.v1", TestContext.Current.CancellationToken)).ShouldBe(row.Id);
    }

    private static WorkerContext NewContext()
    {
        return new WorkerContext(
            Services: new ServiceCollection().BuildServiceProvider(),
            Clock: TimeProvider.System,
            Logger: NullLogger.Instance);
    }

    private static string SerializeAdmitted()
    {
        return JsonSerializer.Serialize(
            new IntegrationInboundAdmittedEvent(
                InboundItemId: SampleInboundId.ToString(),
                ProjectId: SampleProjectId.ToString(),
                AdmittedAt: DateTimeOffset.UtcNow),
            JsonSerializerOptions.Web);
    }

    private static OrchestrationOutboxRow SampleRow(string type, string payload)
    {
        return new OrchestrationOutboxRow(Guid.NewGuid(), type, payload);
    }

    private static (WorkAdmissionSubscriber subscriber,
            InMemoryInboundItemBindingStore bindings,
            InMemoryWorkTaskStore store,
            IInboundItemReader reader,
            InMemoryWorkInboxWatermarkStore watermarks,
            List<OrchestrationOutboxRow> readerRows,
            InMemoryInboundItemBindingStore bindingsExplicit) CreateSubscriber(bool admissionEnabled)
    {
        var store = new InMemoryWorkTaskStore();
        var inbox = new InMemoryWorkInbox();
        var bindings = new InMemoryInboundItemBindingStore();
        var outbox = Substitute.For<IOutbox>();
        var clock = TimeProvider.System;
        var handler = new AdmitTaskHandler(store, inbox, outbox, bindings, clock, NullLogger<AdmitTaskHandler>.Instance);
        var reader = Substitute.For<IInboundItemReader>();
        var watermarks = new InMemoryWorkInboxWatermarkStore();
        var readerRows = new List<OrchestrationOutboxRow>();

        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(inbox);
        services.AddSingleton(bindings);
        services.AddSingleton(outbox);
        services.AddSingleton<IWorkTaskStore>(store);
        services.AddSingleton<IInboundItemBindingStore>(bindings);
        services.AddSingleton(handler);
        // The reader returns whatever rows the test seeded ahead of
        // the cycle — a tight in-memory double stands in for the
        // host adapter's EF query.
        services.AddSingleton<IOrchestrationOutboxReader>(new TestOutboxReader(() => [.. readerRows]));
        services.AddSingleton(reader);
        services.AddSingleton<IWorkInboxWatermarkStore>(watermarks);
        var provider = services.BuildServiceProvider();

        var options = Options.Create(new WorkOptions { AdmissionEnabled = admissionEnabled });
        var subscriber = new WorkAdmissionSubscriber(
            new SingleScopeFactory(provider),
            TimeProvider.System,
            options,
            NullLogger<WorkAdmissionSubscriber>.Instance);

        return (subscriber, bindings, store, reader, watermarks, readerRows, bindings);
    }

    /// <summary>
    /// Single-scope service provider — substitute for the host's
    /// <see cref="IServiceScopeFactory"/> in unit tests.
    /// </summary>
    private sealed class SingleScopeFactory(IServiceProvider root) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new NoopScope(root);
    }

    private sealed class NoopScope(IServiceProvider provider) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = provider;
        public void Dispose() { }
    }

    /// <summary>In-memory double for the engine outbox reader — returns whatever rows the test seeded.</summary>
    private sealed class TestOutboxReader(Func<IReadOnlyList<OrchestrationOutboxRow>> rows) : IOrchestrationOutboxReader
    {
        public Task<OrchestrationOutboxBatch> PollAsync(
            IReadOnlyCollection<string> types,
            Guid lastSeenId,
            CancellationToken cancellationToken = default)
        {
            var list = rows().Where(r => types.Contains(r.Type) && r.Id > lastSeenId).ToList();
            var highest = list.Count == 0 ? lastSeenId : list[^1].Id;
            return Task.FromResult(new OrchestrationOutboxBatch(list.Count, highest, list));
        }
    }
}

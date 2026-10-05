using System.Diagnostics;
using System.Linq.Expressions;
using Comuki.Modules.Observability.Domain.Logs;
using Comuki.Modules.Observability.Infrastructure.VictoriaLogs;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Comuki.Modules.Observability.Unit;

/// <summary>
/// Unit tests for <see cref="VictoriaLogsQueryClient"/>: the
/// ambient-trace-id filter append + the explicit-override-wins rule
/// per <c>specs/observability/spec.md</c> "Trace correlation by default".
/// The tests stub the Refit-generated proxy through the public
/// <c>internal</c> test-ctor (the typed <see cref="IVictoriaLogsApi"/>
/// lives in <c>InternalsVisibleTo</c> territory).
/// </summary>
public sealed class VictoriaLogsQueryClientShould
{
    /// <summary>Explicit trace-id override is honoured verbatim, no ambient append.</summary>
    [Fact(DisplayName = "Given an explicit TraceId in the query, when SearchAsync, the body has that trace_id")]
    public async Task ExplicitTraceIdIsAppendedVerbatimAsync()
    {
        var api = Substitute.For<IVictoriaLogsApi>();
        api.QueryAsync(Arg.Any<LogsQlQueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LogQueryResponseEnvelope("success"));

        using var _ = ActivityListenerStub.NoAmbient();
        var client = new VictoriaLogsQueryClient(
            api,
            NullLogger<VictoriaLogsQueryClient>.Instance);

        await client.SearchAsync(
            new LogsQuery(Query: "level:error", TraceId: "explicit-trace-001"),
            CancellationToken.None);

        await api.Received(1).QueryAsync(
            Arg.Is(QueryContains("trace_id:explicit-trace-001")),
            Arg.Any<CancellationToken>());
    }

    /// <summary>No explicit trace id + ambient Activity → the ambient trace id is appended.</summary>
    [Fact(DisplayName = "Given ambient Activity.TraceId, when SearchAsync, the body has trace_id:ambient")]
    public async Task AmbientTraceIdIsAppendedAsync()
    {
        var api = Substitute.For<IVictoriaLogsApi>();
        api.QueryAsync(Arg.Any<LogsQlQueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LogQueryResponseEnvelope("success"));

        const string AmbientId = "0123456789abcdef0123456789abcdef";
        using var _ = ActivityListenerStub.WithTraceId(AmbientId);

        var client = new VictoriaLogsQueryClient(
            api,
            NullLogger<VictoriaLogsQueryClient>.Instance);

        await client.SearchAsync(
            new LogsQuery(Query: "level:warn"),
            CancellationToken.None);

        await api.Received(1).QueryAsync(
            Arg.Is(QueryContains($"trace_id:{AmbientId}")),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Explicit override always wins over the ambient — no double-append.</summary>
    [Fact(DisplayName = "Given both ambient Activity.TraceId and explicit TraceId, when SearchAsync, the explicit wins")]
    public async Task ExplicitOverrideWinsOverAmbientAsync()
    {
        var api = Substitute.For<IVictoriaLogsApi>();
        api.QueryAsync(Arg.Any<LogsQlQueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LogQueryResponseEnvelope("success"));

        using var ambient = ActivityListenerStub.WithTraceId("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var client = new VictoriaLogsQueryClient(
            api,
            NullLogger<VictoriaLogsQueryClient>.Instance);

        await client.SearchAsync(
            new LogsQuery(Query: "level:info", TraceId: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"),
            CancellationToken.None);

        await api.Received(1).QueryAsync(
            Arg.Is<LogsQlQueryRequest>(static request =>
                request.Query.Contains("trace_id:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb") &&
                !request.Query.Contains("trace_id:aaaa")),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Re-appending the same trace id is idempotent — the loop doesn't pile up.</summary>
    [Fact(DisplayName = "Given a query already containing trace_id, when SearchAsync, no duplicate")]
    public async Task IdempotentTraceAppendAsync()
    {
        var api = Substitute.For<IVictoriaLogsApi>();
        api.QueryAsync(Arg.Any<LogsQlQueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LogQueryResponseEnvelope("success"));

        using var _ = ActivityListenerStub.NoAmbient();
        var client = new VictoriaLogsQueryClient(
            api,
            NullLogger<VictoriaLogsQueryClient>.Instance);

        await client.SearchAsync(
            new LogsQuery(Query: "level:warn trace_id:zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz"),
            CancellationToken.None);

        await api.Received(1).QueryAsync(
            Arg.Is<LogsQlQueryRequest>(static request =>
                request.Query.Replace("trace_id:", string.Empty).Length == request.Query.Length - "trace_id:".Length),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// NSubstitute Arg.Is matcher: query string contains the given needle.
    /// Returns an <see cref="Expression{TDelegate}"/> because
    /// NSubstitute 5.x's <c>Arg.Is&lt;T&gt;</c> only accepts an
    /// expression-tree predicate (not a plain <c>Func</c>). Plain C#
    /// operators only — no <c>is</c> patterns, which the expression tree
    /// compiler refuses.
    /// </summary>
    private static Expression<Predicate<LogsQlQueryRequest>> QueryContains(string needle)
    {
        return request => request.Query != null && request.Query.Contains(needle);
    }

    /// <summary>
    /// Activity fixture stub: an xUnit v3 test must register an
    /// <see cref="ActivityListener"/> before <see cref="Activity.Current"/>
    /// is observable, and the listener must be disposed to keep
    /// tests in the same process isolated. We use
    /// <see cref="Activity.SetParentId(string)"/> — the documented
    /// OTel API for adopting a trace context — because
    /// <see cref="ActivityTraceId"/>'s ctor is internal.
    /// </summary>
    private sealed class ActivityListenerStub : IDisposable
    {
        private static readonly ActivityListener listener = new()
        {
            ShouldListenTo = static _ => true,
            Sample = static (ref _) => ActivitySamplingResult.AllData,
            ActivityStopped = static _ => { },
        };

        private readonly Activity? activityField;
        private readonly bool ownsListenerField;

        private ActivityListenerStub(Activity? activity, bool ownsListener)
        {
            activityField = activity;
            ownsListenerField = ownsListener;
        }

        public static ActivityListenerStub WithTraceId(string traceId)
        {
            ActivitySource.AddActivityListener(listener);
            var source = new ActivitySource("observability.test");
            // Activity.SetParentId only sets the parent context, NOT the
            // activity's own TraceId — which is what the typed client
            // threads onto the wire. Build an explicit ActivityContext
            // with the requested TraceId so Activity.Current?.TraceId
            // returns it.
            var activityTraceId = ActivityTraceId.CreateFromString(traceId);
            var activityContext = new ActivityContext(
                activityTraceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
            var activity = source.CreateActivity("test", ActivityKind.Internal, activityContext);
            activity!.Start();
            return new ActivityListenerStub(activity, ownsListener: true);
        }

        public static ActivityListenerStub NoAmbient()
        {
            // Just to keep the listener alive for the lifetime of this
            // test method, so Activity.Current is observable but ambient
            // (i.e. null / no trace). The listener is shared + never disposed
            // — that's fine, dispose happens at process shutdown.
            ActivitySource.AddActivityListener(listener);
            return new ActivityListenerStub(activity: null, ownsListener: false);
        }

        public void Dispose()
        {
            activityField?.Stop();
            if (ownsListenerField)
            {
                listener.Dispose();
            }
        }
    }
}

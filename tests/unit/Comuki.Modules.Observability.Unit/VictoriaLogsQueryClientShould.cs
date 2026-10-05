using System.Diagnostics;
using System.Net;
using Comuki.Modules.Observability.Domain.Logs;
using Comuki.Modules.Observability.Infrastructure.VictoriaLogs;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Refit;
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
        const string explicitTrace = "explicit-trace-001";
        var api = Substitute.For<IVictoriaLogsApi>();
        api.QueryAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(NewResponse(""));

        using var _ = ActivityListenerStub.NoAmbient();
        var client = new VictoriaLogsQueryClient(
            api,
            NullLogger<VictoriaLogsQueryClient>.Instance);

        await client.SearchAsync(
            new LogsQuery(Query: "level:error", TraceId: explicitTrace),
            CancellationToken.None);

        await api.Received(1).QueryAsync(
            Arg.Is<string>(static q => q.Contains("trace_id:" + explicitTrace)),
            Arg.Any<int?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>No explicit trace id + ambient Activity → the ambient trace id is appended.</summary>
    [Fact(DisplayName = "Given ambient Activity.TraceId, when SearchAsync, the body has trace_id:ambient")]
    public async Task AmbientTraceIdIsAppendedAsync()
    {
        const string AmbientId = "0123456789abcdef0123456789abcdef";
        var api = Substitute.For<IVictoriaLogsApi>();
        api.QueryAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(NewResponse(""));

        using var _ = ActivityListenerStub.WithTraceId(AmbientId);

        var client = new VictoriaLogsQueryClient(
            api,
            NullLogger<VictoriaLogsQueryClient>.Instance);

        await client.SearchAsync(
            new LogsQuery(Query: "level:warn"),
            CancellationToken.None);

        await api.Received(1).QueryAsync(
            Arg.Is<string>(static q => q.Contains("trace_id:" + AmbientId)),
            Arg.Any<int?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Explicit override always wins over the ambient — no double-append.</summary>
    [Fact(DisplayName = "Given both ambient Activity.TraceId and explicit TraceId, when SearchAsync, the explicit wins")]
    public async Task ExplicitOverrideWinsOverAmbientAsync()
    {
        const string explicitTrace = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string ambientTrace = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var api = Substitute.For<IVictoriaLogsApi>();
        api.QueryAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(NewResponse(""));

        using var ambient = ActivityListenerStub.WithTraceId(ambientTrace);
        var client = new VictoriaLogsQueryClient(
            api,
            NullLogger<VictoriaLogsQueryClient>.Instance);

        await client.SearchAsync(
            new LogsQuery(Query: "level:info", TraceId: explicitTrace),
            CancellationToken.None);

        await api.Received(1).QueryAsync(
            Arg.Is<string>(static q => q.Contains("trace_id:" + explicitTrace) && !q.Contains("trace_id:" + ambientTrace)),
            Arg.Any<int?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Re-appending the same trace id is idempotent — the loop doesn't pile up.</summary>
    [Fact(DisplayName = "Given a query already containing trace_id, when SearchAsync, no duplicate")]
    public async Task IdempotentTraceAppendAsync()
    {
        const string explicitTrace = "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz";
        var api = Substitute.For<IVictoriaLogsApi>();
        api.QueryAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(NewResponse(""));

        using var _ = ActivityListenerStub.NoAmbient();
        var client = new VictoriaLogsQueryClient(
            api,
            NullLogger<VictoriaLogsQueryClient>.Instance);

        await client.SearchAsync(
            new LogsQuery(Query: $"level:warn trace_id:{explicitTrace}"),
            CancellationToken.None);

        await api.Received(1).QueryAsync(
            Arg.Is<string>(static q => CountOccurrences(q, "trace_id:") == 1),
            Arg.Any<int?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Build a minimal <see cref="IApiResponse{T}"/> carrying the wire
    /// body the typed client maps. The unit tests exercise the
    /// ambient-trace-id logic and don't need a full Refit surface —
    /// the body is what the typed client reads through
    /// <see cref="VictoriaLogsQueryClient.MapResponse"/>; status and
    /// headers stay neutral. The 5-arg
    /// <c>Refit.ApiResponse&lt;T&gt;</c> ctor is the one a test can
    /// actually wire — its 3-arg sibling demands an associated
    /// <c>HttpRequestMessage</c> as a runtime invariant the Refit
    /// pipeline upholds, so we pass the request explicitly.
    /// </summary>
    private static IApiResponse<string> NewResponse(string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/select/logsql/query");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
        };
        return new ApiResponse<string>(request, response, body, settings: null!, error: null);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    /// <summary>
    /// Activity fixture stub: an xUnit v3 test must register an
    /// <see cref="ActivityListener"/> before <see cref="Activity.Current"/>
    /// is observable, and the listener must be disposed to keep
    /// tests in the same process isolated. We build an
    /// <see cref="ActivityContext"/> with the requested
    /// <see cref="ActivityTraceId"/> so <c>Activity.Current?.TraceId</c>
    /// returns it (Activity.SetParentId only sets the parent context,
    /// not the activity's own TraceId).
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

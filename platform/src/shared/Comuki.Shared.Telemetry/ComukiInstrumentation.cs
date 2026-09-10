namespace Comuki.Shared.Telemetry;

/// <summary>
/// Stable names of every Comuki meter, activity source, instrument and span.
/// One place so emitters (engine modules, host) and the SDK subscriber
/// (<see cref="Installers.ComukiTelemetryInstaller"/>) never drift apart.
/// Metric names are lowercase dot.case with the <c>comuki.</c> prefix;
/// activity source names follow the assembly that emits the span.
/// </summary>
public static class ComukiInstrumentation
{
    // Meters (subscribed by the telemetry installer).
    public const string QueueMeterName = "comuki.queue";
    public const string RunsMeterName = "comuki.runs";
    public const string ComputeMeterName = "comuki.compute";
    public const string ArtifactsMeterName = "comuki.artifacts";

    // Activity sources — one per emitting assembly.
    public const string OrchestrationSourceName = "Comuki.Engine.Orchestration";
    public const string ComputeSourceName = "Comuki.Engine.Compute";
    public const string HostSourceName = "Comuki.Host";

    // Queue instruments + span.
    public const string ClaimSpanName = "comuki.queue.claim";
    public const string ClaimDurationName = "comuki.queue.claim.duration";
    public const string ClaimedCountName = "comuki.queue.claimed";

    // Runs instruments + span.
    public const string ApplyPlanSpanName = "comuki.runs.apply_plan";
    public const string RunStartedName = "comuki.runs.started";
    public const string WorkItemsQueuedName = "comuki.runs.work_items.queued";

    // Compute instruments + spans.
    public const string WorkerStartSpanName = "comuki.compute.worker.start";
    public const string WorkerStopSpanName = "comuki.compute.worker.stop";
    public const string WorkerStartDurationName = "comuki.compute.worker.start.duration";
    public const string WorkersStartedName = "comuki.compute.workers.started";
    public const string WorkersStoppedName = "comuki.compute.workers.stopped";

    // Artifacts instruments.
    public const string ArtifactBundleDelayName = "comuki.artifact.bundle.delay_seconds";
    public const string ArtifactBundleWrittenName = "comuki.artifact.bundle.written";

    // Visual-artifact instruments (issue #51 slice 1).
    public const string VisualArtifactPublishedName = "comuki.artifact.visual.published";
    public const string VisualArtifactBytesName = "comuki.artifact.visual.bytes";

    // Project settings cache fallback instrument (Q27 / v1.1).
    public const string ProjectSettingsCacheFallbackName = "comuki.projectsettings.cache.fallback_total";
    public const string ProjectsMeterName = "comuki.projects";

    // Span/metric tags — bounded cardinality only (no ids, no free text).
    public const string OutcomeTag = "outcome";
    public const string ProviderTag = "provider";
    public const string ProfileTag = "profile";
    public const string ReasonTag = "reason";
    public const string ContentTypeTag = "content_type";
    public const string SourceTag = "source";

    /// <summary>A claim matched a queued work item.</summary>
    public const string OutcomeHit = "hit";

    /// <summary>The queue had nothing for the worker — the normal poll path.</summary>
    public const string OutcomeEmpty = "empty";

    /// <summary>Visual-artifact source — a worker uploaded it.</summary>
    public const string SourceWorker = "worker";

    /// <summary>Visual-artifact source — the brain / host tool uploaded it.</summary>
    public const string SourceBrain = "brain";
}

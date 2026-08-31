using Refit;

namespace Comuki.Host.Translator.Api;

/// <summary>
/// The orchestrator's worker-facing REST surface (claim / heartbeat /
/// complete / fail), called by the translator loop. Responses are wrapped
/// in Refit's <see cref="IApiResponse"/> shapes so 204 (nothing to claim)
/// and 409 (ownership lost) are values, not exceptions.
/// </summary>
public interface IOrchestratorApi
{
    /// <summary>Claims the oldest queued item matching the labels; 204-body-null when nothing matches.</summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [Post("/workers/claim")]
    public Task<ApiResponse<ClaimedWorkItemResponse?>> ClaimAsync(
        [Body] ClaimWorkItemRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Extends the lease; false-status when ownership is gone (409).</summary>
    /// <param name="workItemId"></param>
    /// <param name="cancellationToken"></param>
    [Post("/workers/{workItemId}/heartbeat")]
    public Task<IApiResponse> HeartbeatAsync(Guid workItemId, CancellationToken cancellationToken = default);

    /// <summary>Completes the item with the result JSON; false-status when ownership is gone.</summary>
    /// <param name="workItemId"></param>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [Post("/workers/{workItemId}/complete")]
    public Task<IApiResponse> CompleteAsync(
        Guid workItemId,
        [Body] CompleteWorkItemRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Fails the item with a reason; false-status when ownership is gone.</summary>
    /// <param name="workItemId"></param>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [Post("/workers/{workItemId}/fail")]
    public Task<IApiResponse> FailAsync(
        Guid workItemId,
        [Body] FailWorkItemRequest request,
        CancellationToken cancellationToken = default);
}

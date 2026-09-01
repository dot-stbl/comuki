using Comuki.Host.Chat.Models;
using Comuki.Modules.Chat.Application.Commands;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Application.Sessions;
using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Modules.Identity.Application.Permissions;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Comuki.Host.Chat.Controllers;

/// <summary>
/// The chat session surface (issue #5 slice B): session lifecycle, message
/// turns and the approve decision. Every action demands
/// <c>chat:use</c>; sessions are owned by the acting subject — another
/// subject's session reads as 404. Typed exceptions become ProblemDetails
/// (404 / 409 / 400) in one place.
/// </summary>
/// <param name="sessions">Session lifecycle service.</param>
/// <param name="resolver">Principal → owned session lookup.</param>
/// <param name="store">Transcript reads.</param>
/// <param name="turns">Turn driver.</param>
[ApiController]
[Route(ApiRoutes.ChatSessions)]
[RequiresPermission("chat:use")]
public sealed class ChatSessionsController(
    ChatSessionService sessions,
    ChatSessionResolver resolver,
    IChatSessionStore store,
    IChatTurnService turns) : ControllerBase
{
    /// <summary>Creates a session for the acting subject.</summary>
    /// <param name="request"></param>
    /// <param name="validator"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost("")]
    [ProducesResponseType<ChatSessionView>(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<ActionResult> CreateAsync(
        [FromBody] CreateChatSessionRequest request,
        [FromServices] IValidator<CreateChatSessionCommand> validator,
        CancellationToken cancellationToken = default)
    {
        return ChatEndpointRunner.ExecuteAsync(async () =>
        {
            var subjectId = ChatSubjects.ResolveSubjectId(User);
            await validator.ValidateAndThrowAsync(new CreateChatSessionCommand(subjectId, request.ProjectId, request.Title), cancellationToken);
            var session = await sessions.CreateAsync(
                subjectId,
                request.ProjectId is { } projectId ? new ProjectId(projectId) : null,
                request.Title,
                cancellationToken);

            return new CreatedResult(
                ApiRoutes.ChatSessions + "/" + session.Id.Value,
                ChatSessionView.Of(session));
        });
    }

    /// <summary>Lists the acting subject's recent active sessions.</summary>
    /// <param name="cancellationToken"></param>
    [HttpGet("")]
    [ProducesResponseType<IReadOnlyList<ChatSessionView>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChatSessionView>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var recent = await sessions.ListRecentAsync(ChatSubjects.ResolveSubjectId(User), cancellationToken);
        return Ok(recent.Select(ChatSessionView.Of).ToArray());
    }

    /// <summary>Reads one session of the acting subject.</summary>
    /// <param name="sessionId"></param>
    /// <param name="cancellationToken"></param>
    [HttpGet("{sessionId:guid}")]
    [ProducesResponseType<ChatSessionView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await resolver.ResolveAsync(sessionId, User, cancellationToken);
        return session is { } found ? Ok(ChatSessionView.Of(found)) : ChatProblems.NotFound(sessionId);
    }

    /// <summary>Posts one user message and runs the graph turn.</summary>
    /// <param name="sessionId"></param>
    /// <param name="request"></param>
    /// <param name="validator"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost("{sessionId:guid}/messages")]
    [ProducesResponseType<ChatTurnResultView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult> PostMessageAsync(
        Guid sessionId,
        [FromBody] PostChatMessageRequest request,
        [FromServices] IValidator<PostChatMessageCommand> validator,
        CancellationToken cancellationToken = default)
    {
        return ChatEndpointRunner.ExecuteAsync(async () =>
        {
            var session = await resolver.ResolveAsync(sessionId, User, cancellationToken);

            if (session is null)
            {
                return ChatProblems.NotFound(sessionId);
            }

            await validator.ValidateAndThrowAsync(new PostChatMessageCommand(sessionId, request.Message), cancellationToken);
            var result = await turns.PostAsync(session, request.Message, cancellationToken);
            return new OkObjectResult(ChatTurnResultView.Of(result));
        });
    }

    /// <summary>Reads a page of the transcript, oldest first.</summary>
    /// <param name="sessionId"></param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize"></param>
    /// <param name="cancellationToken"></param>
    [HttpGet("{sessionId:guid}/messages")]
    [ProducesResponseType<ChatMessagesPageView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListMessagesAsync(
        Guid sessionId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var session = await resolver.ResolveAsync(sessionId, User, cancellationToken);

        if (session is null)
        {
            return ChatProblems.NotFound(sessionId);
        }

        var messagesPage = await store.ReadPageAsync(new ChatSessionId(sessionId), page, pageSize, cancellationToken);
        return Ok(ChatMessagesPageView.Of(messagesPage));
    }

    /// <summary>Resolves the pending plan approve interrupt.</summary>
    /// <param name="sessionId"></param>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    [HttpPost("{sessionId:guid}/approve")]
    [ProducesResponseType<ChatTurnResultView>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult> ApproveAsync(
        Guid sessionId,
        [FromBody] ChatApproveRequest request,
        CancellationToken cancellationToken = default)
    {
        return ChatEndpointRunner.ExecuteAsync(async () =>
        {
            var session = await resolver.ResolveAsync(sessionId, User, cancellationToken);

            if (session is null)
            {
                return ChatProblems.NotFound(sessionId);
            }

            var result = await turns.ApproveAsync(session, request.Approved, request.Reason, cancellationToken);
            return new OkObjectResult(ChatTurnResultView.Of(result));
        });
    }
}

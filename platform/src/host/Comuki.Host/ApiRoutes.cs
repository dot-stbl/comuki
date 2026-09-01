namespace Comuki.Host;

/// <summary>Host route templates - the single source for endpoint mapping; no route literals in Map* calls.</summary>
public static class ApiRoutes
{
    public const string Health = "/health";

    public const string Profiles = "/profiles";

    public const string ProfileByKey = "/profiles/{key}";

    public const string ChatCommands = "/chat-commands";

    /// <summary>API root prefix of the versioned auth surface.</summary>
    public const string AuthRoot = "api/v1/auth";

    /// <summary>Base of the per-provider OIDC routes (start endpoint and the handler-owned callback path).</summary>
    public const string AuthOidcRoot = "api/v1/auth/oidc";

    public const string Projects = "/api/v1/projects";

    /// <summary>Base of the chat session surface (issue #5 slice B).</summary>
    public const string ChatSessions = "/api/v1/chat/sessions";

    /// <summary>One chat session by id.</summary>
    public const string ChatSession = "/api/v1/chat/sessions/{sessionId:guid}";

    /// <summary>Messages of one chat session (post a turn / read the transcript).</summary>
    public const string ChatSessionMessages = "/api/v1/chat/sessions/{sessionId:guid}/messages";

    /// <summary>Approve/reject the pending plan interrupt of a session.</summary>
    public const string ChatSessionApprove = "/api/v1/chat/sessions/{sessionId:guid}/approve";

    /// <summary>Merged slash-command catalog (built-ins + control-plane pack).</summary>
    public const string ChatSlash = "/api/v1/chat/slash";

    public const string WorkerClaim = "/workers/claim";

    public const string WorkerHeartbeat = "/workers/{workItemId}/heartbeat";

    public const string WorkerComplete = "/workers/{workItemId}/complete";

    public const string WorkerFail = "/workers/{workItemId}/fail";
}

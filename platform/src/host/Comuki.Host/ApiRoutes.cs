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

    /// <summary>Run listing surface (filter/sort DSL, permission run:read).</summary>
    public const string Runs = "/api/v1/runs";

    /// <summary>One run by id — operator approval (release the escalation gate).</summary>
    public const string RunApprove = "/api/v1/runs/{runId:guid}/approve";

    /// <summary>One run by id — operator cancellation (tear the run down).</summary>
    public const string RunCancel = "/api/v1/runs/{runId:guid}/cancel";

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

    /// <summary>Root of the anonymous tracker webhook surface (issue #6): /api/hooks/{provider}/{key}.</summary>
    public const string HooksRoot = "api/hooks";

    /// <summary>Native ticket creation (permission run:create).</summary>
    public const string Tickets = "/api/v1/tickets";

    /// <summary>Base of the inbox surface (pending list, catalog, claim).</summary>
    public const string Inbox = "/api/v1/inbox";

    /// <summary>Source connection CRUD base.</summary>
    public const string Sources = "/api/v1/sources";

    /// <summary>One source connection by id.</summary>
    public const string Source = "/api/v1/sources/{sourceId:guid}";

    /// <summary>Admission rule CRUD base.</summary>
    public const string AdmissionRules = "/api/v1/admission-rules";

    /// <summary>One admission rule by id.</summary>
    public const string AdmissionRule = "/api/v1/admission-rules/{ruleId:guid}";

    /// <summary>The realtime hub of runs and project attention (issue #7).</summary>
    public const string HubsRuns = "/hubs/runs";

    /// <summary>Cost rollup for one project (permission cost:read).</summary>
    public const string ProjectCosts = "/api/v1/projects/{projectId:guid}/costs";

    /// <summary>Artifact bundle pointers for one run (permission run:read).</summary>
    public const string RunArtifacts = "/api/v1/projects/{projectId:guid}/runs/{runId:guid}/artifacts";

    /// <summary>Knowledge ingest endpoint — operator-triggered; permission knowledge:write.</summary>
    public const string KnowledgeIngest = "/api/v1/knowledge/ingest";

    /// <summary>MCP JSON-RPC 2.0 endpoint — cookie / api-key auth like the rest of the host.</summary>
    public const string Mcp = "/api/v1/mcp";
}

namespace Comuki.Host;

/// <summary>Host route templates - the single source for endpoint mapping; no route literals in Map* calls.</summary>
public static class ApiRoutes
{
    public const string Health = "/health";

    public const string Profiles = "/profiles";

    public const string ProfileByKey = "/profiles/{key}";

    public const string ChatCommands = "/chat-commands";

    public const string WorkerClaim = "/workers/claim";

    public const string WorkerHeartbeat = "/workers/{workItemId}/heartbeat";

    public const string WorkerComplete = "/workers/{workItemId}/complete";

    public const string WorkerFail = "/workers/{workItemId}/fail";
}

namespace Comuki.Modules.Chat.Application.Ports;

/// <summary>
/// One chat tool invocation issued by the graph (runs list, ticket create,
/// run stop). <paramref name="ArgumentsJson"/> is the JSON-encoded argument
/// object; each tool documents its own shape.
/// </summary>
/// <param name="Name">Tool name: <c>runs</c>, <c>create_ticket</c>, <c>stop_run</c>.</param>
/// <param name="ArgumentsJson">Tool arguments as a JSON object string.</param>
public sealed record ChatToolCall(string Name, string ArgumentsJson);

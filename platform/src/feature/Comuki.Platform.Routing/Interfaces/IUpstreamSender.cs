using Comuki.Platform.Routing.Forwarding;
using Microsoft.AspNetCore.Http;

namespace Comuki.Platform.Routing.Interfaces;

/// <summary>
/// Одна попытка форварда запроса на апстрим Z.AI с конкретным ключом.
/// Реализация решает: успех (стримит клиенту), исчерпание (ничего не пишет),
/// либо не-quota ошибка (пробрасывает клиенту).
/// </summary>
public interface IUpstreamSender
{
    public Task<UpstreamSendResult> SendOnceAsync(HttpContext context, string apiKey, CancellationToken cancellationToken);
}

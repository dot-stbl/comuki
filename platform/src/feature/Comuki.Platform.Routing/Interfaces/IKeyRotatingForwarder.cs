using Microsoft.AspNetCore.Http;

namespace Comuki.Platform.Routing.Interfaces;

/// <summary>
/// Точка входа прокси: форвардит запрос агента на Z.AI, прозрачно ротируя
/// исчерпанные ключи. Пишет ответ (или ошибку) напрямую в HttpContext.Response.
/// </summary>
public interface IKeyRotatingForwarder
{
    public Task ForwardAsync(HttpContext context, CancellationToken cancellationToken);
}

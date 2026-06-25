using Comuki.Platform.Routing.Installers;
using Comuki.Platform.Routing.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseDefaultServiceProvider((context, options) =>
{
    options.ValidateScopes = context.HostingEnvironment.IsDevelopment();
    options.ValidateOnBuild = true;
});

builder.Services.AddRoutingCore(builder.Configuration);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Catch-all: всё остальное идёт через форвардер с ротацией ключей.
// Литеральный /health имеет приоритет над catch-all в маршрутизации.
app.Map(
    "/{**catchAll}",
    (HttpContext context, IKeyRotatingForwarder forwarder, CancellationToken cancellationToken)
        => forwarder.ForwardAsync(context, cancellationToken));

await app.RunAsync();

// Доступ для WebApplicationFactory в интеграционных тестах.
public partial class Program;

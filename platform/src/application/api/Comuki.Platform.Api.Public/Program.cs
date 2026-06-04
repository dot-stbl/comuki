using Comuki.Platform.Orchestration;
using Comuki.Platform.Orchestration.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IOrchestrationService, NoOpOrchestrationService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

await app.RunAsync();

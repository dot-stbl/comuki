using Comuki.Platform.Orchestration;
using Comuki.Platform.Orchestration.Interfaces;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IOrchestrationService, NoOpOrchestrationService>();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // OpenAPI spec served at /openapi/v1.json (runtime, no build-time codegen needed for dev).
    app.MapOpenApi();
    // Scalar UI at /scalar/v1 — modern API reference, replaces Swagger UI per Phase 2 plan.
    app.MapScalarApiReference();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

await app.RunAsync();

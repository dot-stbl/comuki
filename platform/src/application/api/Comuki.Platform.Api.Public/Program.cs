using Comuki.Platform.Api.Contracts;
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

// Typed endpoint — gives Kubb a schema to generate. Real routes land in Phase 3+.
app.MapGet("/api/v1/info", () => Results.Ok(new InfoResponse("Comuki", "1.0.0", "Phase 2 — Stack Foundation")))
    .Produces<InfoResponse>();

await app.RunAsync();

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health", static () => Results.Ok(new { status = "ok" }));

await app.RunAsync();

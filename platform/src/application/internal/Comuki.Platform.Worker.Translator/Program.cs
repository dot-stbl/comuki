using Comuki.Platform.Worker.Translator;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<TranslatorHostedService>();

var host = builder.Build();
await host.RunAsync();

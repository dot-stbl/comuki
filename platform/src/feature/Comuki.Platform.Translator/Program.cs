using Comuki.Platform.Translator;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<TranslatorHostedService>();

var host = builder.Build();
await host.RunAsync();

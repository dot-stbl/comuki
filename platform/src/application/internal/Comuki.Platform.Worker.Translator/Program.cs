using Comuki.Platform.Worker.Translator;
using Comuki.Platform.Worker.Translator.Interfaces;
using Comuki.Platform.Worker.Translator.Services;

var builder = Host.CreateApplicationBuilder(args);

// IPiRunner is configured with the production executable. The default is "pi"
// (the path is on $PATH inside the worker container, which sets
// $PATH=/usr/local/bin after `bun add -g`). Tests instantiate PiRunner
// directly with a different executable path — they don't go through DI.
var piExecutable = builder.Configuration["Translator:Executable"] ?? "pi";
builder.Services.AddSingleton<IPiRunner>(_ => new PiRunner(piExecutable, _.GetRequiredService<ILogger<PiRunner>>()));
builder.Services.AddSingleton<ITranslator, WorkerTranslator>();
builder.Services.AddHostedService<TranslatorHostedService>();

var host = builder.Build();
await host.RunAsync();

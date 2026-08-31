using Comuki.Host;

var app = HostComposer.Compose(WebApplication.CreateBuilder(args));

await app.RunAsync();

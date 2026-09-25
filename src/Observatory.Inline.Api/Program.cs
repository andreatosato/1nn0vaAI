using Observatory.Api;
using Observatory.Core;
using Observatory.Inline.Api;

var builder = DemoApiApplication.CreateBuilder(args, DemoTechnologies.Inline);
builder.Services.AddSingleton<IAgentRuntime, InlineAgent>();
var app = await DemoApiApplication.BuildAsync(builder);
await app.RunAsync();

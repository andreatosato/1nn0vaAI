using Observatory.Api;
using Observatory.Core;
using Observatory.Router.Api;

var builder = DemoApiApplication.CreateBuilder(args, DemoTechnologies.A2A);
builder.Services.AddSingleton<IAgentRuntime, RouterAgent>();
var app = await DemoApiApplication.BuildAsync(builder);
await app.RunAsync();

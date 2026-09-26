using Observatory.Api;
using Observatory.Core;
using Observatory.Skills.Api;

var builder = DemoApiApplication.CreateBuilder(args, DemoTechnologies.Skills);
builder.Services.AddSingleton<IAgentRuntime, SkillsAgent>();
var app = await DemoApiApplication.BuildAsync(builder);
await app.RunAsync();

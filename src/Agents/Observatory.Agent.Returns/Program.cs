using Observatory.AgentHost;
using Observatory.Agents;
using Observatory.Returns.Api;

var builder = AgentHostApplication.CreateBuilder(args);
builder.Services.AddSingleton<ISpecialistAgent, ReturnsAgent>();
var app = AgentHostApplication.Build(builder, AgentNames.Returns, ReturnsEndpoints.MapReturns);
await app.RunAsync();

using Observatory.AgentHost;
using Observatory.Agents;
using Observatory.Orders.Api;

var builder = AgentHostApplication.CreateBuilder(args);
builder.Services.AddSingleton<ISpecialistAgent, OrdersAgent>();
var app = AgentHostApplication.Build(builder, AgentNames.Orders, OrdersEndpoints.MapOrders);
await app.RunAsync();

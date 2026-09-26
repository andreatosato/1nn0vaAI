using Observatory.AgentHost;
using Observatory.Agents;
using Observatory.Catalog.Api;

var builder = AgentHostApplication.CreateBuilder(args);
builder.Services.AddSingleton<ISpecialistAgent, CatalogAgent>();
var app = AgentHostApplication.Build(builder, AgentNames.Catalog, CatalogEndpoints.MapCatalog);
await app.RunAsync();

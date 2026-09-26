using Observatory.Agent.Catalog;
using Observatory.SpecialistHost;

// Catalog specialist agent: its own process, model, instructions and tools. Invoked by the A2A router.
var builder = SpecialistHostApplication.CreateBuilder<CatalogAgent>(args);
var app = SpecialistHostApplication.Build(builder);
app.Run();

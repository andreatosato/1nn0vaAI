using Observatory.Agent.Returns;
using Observatory.SpecialistHost;

// Returns specialist agent: its own process, model, instructions and tools. Invoked by the A2A router.
var builder = SpecialistHostApplication.CreateBuilder<ReturnsAgent>(args);
var app = SpecialistHostApplication.Build(builder);
app.Run();

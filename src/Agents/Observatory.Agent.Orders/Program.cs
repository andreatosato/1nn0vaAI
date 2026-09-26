using Observatory.Agent.Orders;
using Observatory.SpecialistHost;

// Orders specialist agent: its own process, model, instructions and tools. Invoked by the A2A router.
var builder = SpecialistHostApplication.CreateBuilder<OrdersAgent>(args);
var app = SpecialistHostApplication.Build(builder);
app.Run();

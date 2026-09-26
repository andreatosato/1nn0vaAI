using Observatory.Router.A2A;
using Observatory.RouterHost;

// A2A router: an agent with a model that delegates to three remote specialist agents over the A2A protocol.
// Each specialist runs in its own process with its own model, instructions and tools.
var builder = RouterHostApplication.CreateBuilder<A2ARouter>(args, A2ARouter.Architecture);
var app = await RouterHostApplication.BuildAsync(builder);
app.Run();

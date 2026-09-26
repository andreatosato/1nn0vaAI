using Observatory.Router.Inline;
using Observatory.RouterHost;

// Inline router: one agent with a model. Its tools call the Catalog, Orders and Returns business APIs over HTTP;
// every integration rule is written inline in its instructions.
var builder = RouterHostApplication.CreateBuilder<InlineRouter>(args, InlineRouter.Architecture);
var app = await RouterHostApplication.BuildAsync(builder);
app.Run();

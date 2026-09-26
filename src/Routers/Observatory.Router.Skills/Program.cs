using Observatory.Router.Skills;
using Observatory.RouterHost;

// Skills router: one agent with a model. It loads the skills of the three remote specialists from their
// skill sites (skill-catalog, skill-orders, skill-returns) on demand and calls the shop HTTP tools itself.
var builder = RouterHostApplication.CreateBuilder<SkillsRouter>(args, SkillsRouter.Architecture);
builder.Services.AddHttpClient("skills");
var app = await RouterHostApplication.BuildAsync(builder);
app.Run();

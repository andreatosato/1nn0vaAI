using Observatory.Core;
using Observatory.Shop.Returns;

// Returns business API: the system of record for return policies and assessments. No model runs here.
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
// Malformed query or body values become 400 problem details instead of an empty response.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddSingleton<IShopData>(_ => new ShopData(
    builder.Configuration["Data:Directory"] ?? Path.Combine(AppContext.BaseDirectory, "data"),
    builder.Configuration["Shop:StatePath"]));

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapDefaultEndpoints();
app.MapReturnsEndpoints();

var shop = app.Services.GetRequiredService<IShopData>();
app.Logger.LogInformation("Returns ready: {PolicyCount} synthetic policies.", shop.Policies.Count);
app.Run();

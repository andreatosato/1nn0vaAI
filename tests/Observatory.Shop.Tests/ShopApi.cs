using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Observatory.Core;

namespace Observatory.Shop.Tests;

/// <summary>
/// Starts one business API in memory with its own empty shop state directory.
/// TEntryPoint is any public type of the API assembly (its DomainExceptionHandler), because Program is shared by name.
/// </summary>
public sealed class ShopApi<TEntryPoint> : WebApplicationFactory<TEntryPoint> where TEntryPoint : class
{
    private readonly string _state = Path.Combine(Path.GetTempPath(), "observatory-shop-tests", Guid.NewGuid().ToString("N"));

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Data:Directory", Path.Combine(_state, "catalog"));
        builder.UseSetting("Shop:StatePath", Path.Combine(_state, "domain"));
    }

    public HttpClient Customer(string? customerId = DemoClock.CustomerId, bool? confirmed = null)
    {
        var client = CreateClient();
        if (customerId is not null) client.DefaultRequestHeaders.TryAddWithoutValidation(ShopHttpContract.CustomerHeader, customerId);
        if (confirmed is not null) client.DefaultRequestHeaders.Add(ShopHttpContract.ConfirmationHeader, confirmed.Value.ToString());
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_state)) Directory.Delete(_state, recursive: true);
    }

    public static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {(int)status} {code}, got {(int)response.StatusCode}: {body}");
        Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }
}

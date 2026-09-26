using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Observatory.Core;
using CatalogApi = Observatory.Shop.Catalog.DomainExceptionHandler;
using static Observatory.Shop.Tests.ShopApi<Observatory.Shop.Catalog.DomainExceptionHandler>;

namespace Observatory.Shop.Tests;

public sealed class CatalogApiTests(ShopApi<CatalogApi> api) : IClassFixture<ShopApi<CatalogApi>>
{
    [Fact]
    public async Task Snapshot_contains_the_frozen_catalog_with_provenance_for_the_ui()
    {
        var snapshot = await api.CreateClient().GetFromJsonAsync<CatalogSnapshot>("/catalog", Json);

        Assert.NotNull(snapshot);
        Assert.Equal(38, snapshot.Products.Count);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.ContentHash));
    }

    [Fact]
    public async Task Product_facts_keep_list_prices_and_never_carry_images()
    {
        var client = api.CreateClient();

        var product = await client.GetFromJsonAsync<ProductFact>("/products/83", Json);
        var search = await client.GetStringAsync("/products?query=camicia&maxPrice=40&take=2");

        Assert.Equal(29.99m, product!.Price);
        Assert.Equal(2, JsonSerializer.Deserialize<ProductFact[]>(search, Json)!.Length);
        Assert.DoesNotContain("thumbnail", search, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"images\"", search, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Query_totals_cover_every_match_while_examples_are_paged()
    {
        var result = await api.CreateClient().GetFromJsonAsync<CatalogQueryResponse>(
            "/catalog/query?category=scarpe&color=rosso&inStockOnly=true&take=1", Json);

        Assert.Equal((4, 4, 93, true), (result!.TotalProducts, result.InStockProducts, result.StockUnits, result.HasMore));
        Assert.Equal(189, Assert.Single(result.Products).Id);
        Assert.Equal(("shoes", "red"), (result.Filters.Category, result.Filters.Color));
    }

    [Theory]
    [InlineData("/products/999999", HttpStatusCode.NotFound, "product_not_found")]
    [InlineData("/products?take=0", HttpStatusCode.BadRequest, "invalid_search")]
    [InlineData("/catalog/query?color=red%20or%20black", HttpStatusCode.BadRequest, "invalid_search")]
    [InlineData("/catalog/query?take=oops", HttpStatusCode.BadRequest, "invalid_request")]
    public async Task Rejections_are_problem_details_with_a_domain_code(string path, HttpStatusCode status, string code)
    {
        using var response = await api.CreateClient().GetAsync(path);

        await AssertProblem(response, status, code);
    }

    [Fact]
    public async Task Serves_only_its_own_domain_and_no_skills()
    {
        var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/orders/ORD-1042")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/skills")).StatusCode);
    }

    [Fact]
    public async Task Exposes_the_aspire_health_endpoints()
    {
        var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/alive")).StatusCode);
    }
}

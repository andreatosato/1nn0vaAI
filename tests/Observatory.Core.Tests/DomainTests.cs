using System.Text.Json;
using Observatory.Core;

namespace Observatory.Tests;

public sealed class DomainTests : IDisposable
{
    private readonly string state = Path.Combine(Path.GetTempPath(), "observatory-tests", Guid.NewGuid().ToString("N"));
    private readonly ShopData shop;

    public DomainTests()
    {
        shop = new ShopData(Path.Combine(AppContext.BaseDirectory, "data"), state);
    }

    [Fact]
    public void Public_catalog_is_frozen_and_attributed()
    {
        Assert.True(shop.Products.Count >= 30);
        Assert.Equal(shop.Products.Count, shop.Products.Select(p => p.Id).Distinct().Count());
        Assert.Equal("DummyJSON", shop.Catalog.Source);
        Assert.StartsWith("https://dummyjson.com/", shop.Catalog.SourceUrl);
        Assert.Equal(64, shop.Catalog.ContentHash.Length);
        Assert.All(shop.Products, p => Assert.StartsWith("https://cdn.dummyjson.com/", p.Thumbnail));
    }

    [Fact]
    public void Agent_product_facts_cannot_contain_images()
    {
        var facts = shop.Products.Select(product => product.ToFact()).ToArray();
        var serialized = JsonSerializer.Serialize(facts);
        Assert.DoesNotContain("thumbnail", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("images", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdn.dummyjson.com", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("base64", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("image_url", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Outlet_change_of_mind_is_denied_after_eighteen_days()
    {
        var result = shop.AssessReturn("ORD-1042", "change-of-mind");
        Assert.False(result.Eligible);
        Assert.Equal(18, result.DaysSinceDelivery);
        Assert.Equal("POL-OUTLET-14", result.PolicyId);
    }

    [Fact]
    public void Defect_overrides_outlet_and_uses_paid_amount()
    {
        var result = shop.AssessReturn("ORD-1042", "defect");
        Assert.True(result.Eligible);
        Assert.Equal("POL-DEFECT-60", result.PolicyId);
        Assert.Equal(19.99m, result.RefundAmount);
        Assert.Equal(29.99m, shop.GetOrder("ORD-1042").ListPrice);
        Assert.Equal("USD", result.Currency);
    }

    [Theory]
    [InlineData("ORD-1001")]
    [InlineData("ORD-9999")]
    public void Unknown_and_other_customer_orders_disclose_no_details(string id)
    {
        var error = Assert.Throws<DomainException>(() => shop.GetOrder(id));
        Assert.Equal("order_not_found", error.Code);
    }

    [Fact]
    public void Missing_reason_requires_clarification()
    {
        var result = shop.AssessReturn("ORD-1042", "unknown");
        Assert.True(result.NeedsClarification);
        Assert.False(result.Eligible);
    }

    [Fact]
    public void No_draft_without_explicit_confirmation()
    {
        var error = Assert.Throws<DomainException>(() => shop.CreateReturnDraft("ORD-1042", "defect", false));
        Assert.Equal("confirmation_required", error.Code);
    }

    [Fact]
    public void Draft_is_idempotent_and_survives_new_domain_instance()
    {
        var first = shop.CreateReturnDraft("ORD-1042", "defect", true);
        var restarted = new ShopData(Path.Combine(AppContext.BaseDirectory, "data"), state);
        var second = restarted.CreateReturnDraft("ORD-1042", "defect", true);
        Assert.Equal(first, second);
        Assert.Equal(19.99m, second.Amount);
        Assert.Equal("draft-synthetic", second.Status);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(24)]
    [InlineData(48)]
    public async Task Concurrent_domain_instances_create_only_one_draft(int concurrency)
    {
        var instances = Enumerable.Range(0, concurrency)
            .Select(_ => new ShopData(Path.Combine(AppContext.BaseDirectory, "data"), state)).ToArray();
        var drafts = await Task.WhenAll(instances.Select(instance =>
            Task.Run(() => instance.CreateReturnDraft("ORD-1042", "defect", true))));
        Assert.Single(drafts.Distinct());
        Assert.Single(Directory.GetFiles(state, "*.json"));
        Assert.Empty(Directory.GetFiles(state, "*.tmp"));
    }

    [Theory]
    [InlineData("ORD-1042", "change-of-mind", "return_not_eligible")]
    [InlineData("ORD-1001", "defect", "order_not_found")]
    public void Confirmation_does_not_override_policy_or_customer_scope(string orderId, string reason, string expectedCode)
    {
        var error = Assert.Throws<DomainException>(() => shop.CreateReturnDraft(orderId, reason, true));
        Assert.Equal(expectedCode, error.Code);
        Assert.Empty(Directory.GetFiles(state));
    }

    [Fact]
    public void Scenarios_have_a_separate_holdout_and_no_golden_data_in_tools()
    {
        Assert.Equal(24, shop.Scenarios.Count);
        Assert.Equal(8, shop.Scenarios.Count(s => s.Split == "holdout"));
        Assert.Equal(6, shop.Scenarios.Single(s => s.Id == "main-six-turns").Turns.Count);
        Assert.DoesNotContain("ExpectedFacts", JsonSerializer.Serialize(shop.SearchProducts()));
    }

    [Fact]
    public void Search_respects_budget_and_preserves_stable_order()
    {
        var matches = shop.SearchProducts("camicia", 30m, 5);
        Assert.NotEmpty(matches);
        Assert.All(matches, product => Assert.True(product.Price <= 30m));
        Assert.Equal(matches, shop.SearchProducts("camicia", 30m, 5));
    }

    [Fact]
    public void Italian_red_clothes_query_distinguishes_models_from_available_pieces()
    {
        var result = shop.QueryCatalog(new() { Category = "vestiti", Color = "rossi", InStockOnly = true });
        Assert.Equal("clothing", result.Filters.Category);
        Assert.Equal("red", result.Filters.Color);
        Assert.Equal(1, result.TotalProducts);
        Assert.Equal(1, result.InStockProducts);
        Assert.Equal(62L, result.StockUnits);
        Assert.Equal(181, Assert.Single(result.Products).Id);
        Assert.Contains("non dalle immagini", result.ColorBasis);
        Assert.Equal(result.Products, shop.SearchProducts("vestiti rossi"));
        Assert.Empty(shop.SearchProducts("camicie rosse"));
    }

    [Fact]
    public void Catalog_totals_cover_every_match_before_take()
    {
        var result = shop.QueryCatalog(new() { Take = 1 });
        Assert.Single(result.Products);
        Assert.Equal(38, result.TotalProducts);
        Assert.Equal(shop.Products.Sum(product => (long)product.Stock), result.StockUnits);
        Assert.Equal(shop.Products.Count(product => product.Stock > 0), result.InStockProducts);
        Assert.True(result.HasMore);
        Assert.Equal(shop.Products.OrderBy(product => product.Price).ThenBy(product => product.Id).First().Id,
            result.Products[0].Id);
    }

    [Fact]
    public void Catalog_filters_are_combined_and_zero_is_not_an_error()
    {
        var result = shop.QueryCatalog(new() { Category = "scarpe", Color = "rosso", MaxPrice = 100m, Take = 1 });
        Assert.Equal(189, Assert.Single(result.Products).Id);
        Assert.Equal(1, result.TotalProducts);
        Assert.Equal(7L, result.StockUnits);
        Assert.False(result.HasMore);
        var empty = shop.QueryCatalog(new() { Category = "vestiti", Color = "rosso", MaxPrice = 100m });
        Assert.Empty(empty.Products);
        Assert.Equal(0, empty.TotalProducts);
        Assert.Equal(0L, empty.StockUnits);
        Assert.False(empty.HasMore);
    }

    [Fact]
    public void Textual_colors_use_whole_words_and_do_not_inspect_images()
    {
        shop.Catalog.Products.Add(new()
        {
            Id = 9001, Title = "Inspired apparel", Description = "A tailored garment", Category = "mens-shirts",
            Price = 1, Stock = 3, Tags = ["clothing"], Images = ["https://example.invalid/red-shirt.jpg"]
        });
        var result = shop.QueryCatalog(new() { Category = "clothing", Color = "rosso" });
        Assert.Equal(181, Assert.Single(result.Products).Id);
        Assert.Equal(1, result.TotalProducts);
    }

    [Fact]
    public void Availability_filter_excludes_zero_stock_without_changing_unfiltered_counts()
    {
        shop.Catalog.Products.Add(new()
        {
            Id = 9002, Title = "Red shirt", Category = "mens-shirts", Price = 1, Stock = 0, Tags = ["clothing"]
        });
        var filters = new CatalogQueryRequest { Category = "clothing", Color = "red" };
        var all = shop.QueryCatalog(filters);
        var available = shop.QueryCatalog(filters with { InStockOnly = true });
        Assert.Equal(2, all.TotalProducts);
        Assert.Equal(1, all.InStockProducts);
        Assert.Equal(62L, all.StockUnits);
        Assert.Equal(1, available.TotalProducts);
        Assert.Equal(62L, available.StockUnits);
    }

    [Fact]
    public void Catalog_facets_are_grounded_and_multicolor_counts_are_not_additive()
    {
        var facets = shop.GetCatalogFacets();
        Assert.Equal(38, facets.TotalProducts);
        Assert.Equal(38, facets.Categories.Sum(facet => facet.ProductCount));
        Assert.Equal(5, facets.Categories.Single(facet => facet.Value == "mens-shirts").ProductCount);
        Assert.Equal(new CatalogFacet("red", 5, 155), facets.Colors.Single(facet => facet.Value == "red"));
        Assert.Contains(shop.QueryCatalog(new() { Color = "black", Category = "clothing" }).Products, product => product.Id == 181);
        Assert.Contains("non si sommano", facets.ColorBasis);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(31, 10)]
    [InlineData(5, -1)]
    public void Invalid_catalog_limits_are_explicit_domain_errors(int take, int maxPrice)
    {
        var error = Assert.Throws<DomainException>(() => shop.QueryCatalog(new() { Take = take, MaxPrice = maxPrice }));
        Assert.Equal("invalid_search", error.Code);
    }

    [Fact]
    public void Invalid_catalog_filter_text_is_not_silently_ignored()
    {
        foreach (var query in new CatalogQueryRequest[]
        {
            new() { Query = new string('x', 513) },
            new() { Category = new string('x', 81) },
            new() { Color = new string('x', 41) },
            new() { Color = "red or black" }
        })
            Assert.Equal("invalid_search", Assert.Throws<DomainException>(() => shop.QueryCatalog(query)).Code);
    }

    [Fact]
    public void Catalog_aggregates_do_not_leak_images_or_scenario_answers()
    {
        var json = JsonSerializer.Serialize(new { query = shop.QueryCatalog(new()), facets = shop.GetCatalogFacets() });
        foreach (var prohibited in new[] { "\"Thumbnail\"", "\"Images\"", "cdn.dummyjson.com", "base64", "ExpectedFacts" })
            Assert.DoesNotContain(prohibited, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Model_registry_contains_exact_requested_families_and_no_fake_deployments()
    {
        Assert.Equal(["gpt5", "gpt6-astra", "gpt6-sol", "gpt6-luna"], ModelCatalog.Defaults.Select(m => m.Id));
        Assert.All(ModelCatalog.Defaults, model =>
        {
            Assert.False(model.Configured);
            Assert.Null(model.Deployment);
            Assert.Null(model.Pricing.InputPerMillion);
        });
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(state))
        {
            File.Delete(file);
        }
        Directory.Delete(state);
    }
}

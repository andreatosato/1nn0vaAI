using System.Text.Json;
using Observatory.Core;

namespace Observatory.Tests;

public sealed class DataInitializationTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string root = Path.Combine(Path.GetTempPath(), "observatory-data-tests", Guid.NewGuid().ToString("N"));
    private readonly string bundledPath = Path.Combine(AppContext.BaseDirectory, "data", "products.snapshot.json");
    private string CatalogDirectory => Path.Combine(root, "catalog");
    private string StateDirectory => Path.Combine(root, "domain");
    private string SnapshotPath => Path.Combine(CatalogDirectory, "products.snapshot.json");

    [Fact]
    public void Empty_directory_is_initialized_from_the_bundled_catalog_with_no_drafts()
    {
        var shop = new ShopData(CatalogDirectory, StateDirectory);

        Assert.Equal(File.ReadAllBytes(bundledPath), File.ReadAllBytes(SnapshotPath));
        Assert.True(shop.Products.Count >= 30);
        Assert.Equal(12, shop.Policies.Count);
        Assert.Equal(24, shop.Scenarios.Count);
        var order = shop.GetOrder("ORD-1042");
        Assert.Equal(83, order.ProductId);
        Assert.Equal(29.99m, order.ListPrice);
        Assert.Equal(19.99m, order.AmountPaid);
        Assert.Empty(Directory.GetFiles(StateDirectory));
        Assert.Single(Directory.GetFiles(CatalogDirectory));
    }

    [Fact]
    public void Restart_preserves_existing_snapshot_provenance_and_domain_state()
    {
        var original = new ShopData(CatalogDirectory, StateDirectory);
        var draft = original.CreateReturnDraft("ORD-1042", "defect", true);
        File.WriteAllText(SnapshotPath, JsonSerializer.Serialize(original.Catalog with
        {
            Notice = "Previously selected catalog; do not replace at startup."
        }, Json));
        File.SetLastWriteTimeUtc(SnapshotPath, DateTime.UtcNow.AddDays(-1));
        var bytes = File.ReadAllBytes(SnapshotPath);
        var writtenAt = File.GetLastWriteTimeUtc(SnapshotPath);
        var draftPath = Assert.Single(Directory.GetFiles(StateDirectory));
        var draftBytes = File.ReadAllBytes(draftPath);

        var restarted = new ShopData(CatalogDirectory, StateDirectory);

        Assert.Equal("Previously selected catalog; do not replace at startup.", restarted.Catalog.Notice);
        Assert.Equal(original.Catalog.ContentHash, restarted.Catalog.ContentHash);
        Assert.Equal(original.Catalog.RetrievedAt, restarted.Catalog.RetrievedAt);
        Assert.Equal(bytes, File.ReadAllBytes(SnapshotPath));
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(SnapshotPath));
        Assert.Equal(draftBytes, File.ReadAllBytes(draftPath));
        Assert.Equal(draft, restarted.CreateReturnDraft("ORD-1042", "defect", true));
        Assert.Single(Directory.GetFiles(StateDirectory));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(12)]
    [InlineData(24)]
    public async Task Concurrent_hosts_publish_one_complete_catalog_without_temporary_files(int concurrency)
    {
        var shops = await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ =>
            Task.Run(() => new ShopData(CatalogDirectory, StateDirectory))));

        Assert.Single(shops.Select(shop => shop.Catalog.ContentHash).Distinct());
        Assert.Equal(File.ReadAllBytes(bundledPath), File.ReadAllBytes(SnapshotPath));
        Assert.Single(Directory.GetFiles(CatalogDirectory));
        Assert.Empty(Directory.GetFiles(StateDirectory));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"products\":[]}")]
    [InlineData("{\"products\":null}")]
    public void Invalid_existing_snapshot_fails_without_replacement(string content)
    {
        Directory.CreateDirectory(CatalogDirectory);
        File.WriteAllText(SnapshotPath, content);

        Assert.Throws<InvalidDataException>(() => new ShopData(CatalogDirectory, StateDirectory));

        Assert.Equal(content, File.ReadAllText(SnapshotPath));
        Assert.Single(Directory.GetFiles(CatalogDirectory));
        Assert.False(Directory.Exists(StateDirectory));
    }

    [Fact]
    public void Malformed_existing_json_fails_without_replacement()
    {
        Directory.CreateDirectory(CatalogDirectory);
        File.WriteAllText(SnapshotPath, "{invalid json");

        Assert.Throws<JsonException>(() => new ShopData(CatalogDirectory, StateDirectory));

        Assert.Equal("{invalid json", File.ReadAllText(SnapshotPath));
        Assert.Single(Directory.GetFiles(CatalogDirectory));
        Assert.False(Directory.Exists(StateDirectory));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing-primary")]
    [InlineData("changed-price")]
    public void Invalid_frozen_scenario_is_rejected_before_domain_initialization(string failure)
    {
        Directory.CreateDirectory(CatalogDirectory);
        var catalog = Assert.IsType<CatalogSnapshot>(
            JsonSerializer.Deserialize<CatalogSnapshot>(File.ReadAllText(bundledPath), Json));
        var primaryIndex = catalog.Products.FindIndex(product => product.Id == 83);
        var primary = catalog.Products[primaryIndex];
        catalog.Products[primaryIndex] = failure switch
        {
            "duplicate" => catalog.Products.First(product => product.Id != 83),
            "missing-primary" => primary with { Id = 9999 },
            _ => primary with { Price = 30m }
        };
        var content = JsonSerializer.Serialize(catalog, Json);
        File.WriteAllText(SnapshotPath, content);

        Assert.Throws<InvalidDataException>(() => new ShopData(CatalogDirectory, StateDirectory));

        Assert.Equal(content, File.ReadAllText(SnapshotPath));
        Assert.Single(Directory.GetFiles(CatalogDirectory));
        Assert.False(Directory.Exists(StateDirectory));
    }

    [Fact]
    public void Invalid_destination_fails_instead_of_using_an_in_memory_fallback()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(CatalogDirectory, "This path is a file, not a directory.");

        Assert.Throws<IOException>(() => new ShopData(CatalogDirectory, StateDirectory));

        Assert.Equal("This path is a file, not a directory.", File.ReadAllText(CatalogDirectory));
        Assert.False(Directory.Exists(StateDirectory));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

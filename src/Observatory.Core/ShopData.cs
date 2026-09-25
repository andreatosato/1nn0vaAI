using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Observatory.Core;

public sealed class ShopData : IShopData
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyDictionary<string, ShopOrder> orders;
    private readonly string stateDirectory;

    public ShopData(string dataDirectory, string? stateDirectory = null)
    {
        Catalog = LoadCatalog(Path.GetFullPath(dataDirectory));
        var primary = Catalog.Products.Single(p => p.Id == 83);
        this.stateDirectory = Path.GetFullPath(stateDirectory ?? Path.Combine(dataDirectory, "state"));
        Directory.CreateDirectory(this.stateDirectory);
        orders = Enumerable.Range(0, 50).Select(index =>
        {
            var product = index == 42 ? primary : Products[index % Products.Count];
            return new ShopOrder(
                $"ORD-{1000 + index}",
                index == 42 ? DemoClock.CustomerId : $"CUST-DEMO-{index % 10 + 1:00}",
                product.Id, product.Title, product.Price,
                index == 42 ? 19.99m : decimal.Round(product.Price * 0.8m, 2, MidpointRounding.AwayFromZero),
                product.Currency, index == 42 || index % 3 == 0,
                index == 42 ? new DateOnly(2026, 9, 5) : DateOnly.FromDateTime(DemoClock.AsOf.UtcDateTime).AddDays(-(index % 45 + 1)),
                "delivered", $"DEMO-TRACK-{1000 + index}");
        }).ToDictionary(order => order.Id, StringComparer.OrdinalIgnoreCase);
    }

    public CatalogSnapshot Catalog { get; }
    public IReadOnlyList<Product> Products => Catalog.Products;
    public IReadOnlyList<ShopOrder> DemoOrders => orders.Values.OrderBy(order => order.Id, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<ShopPolicy> Policies { get; } =
    [
        new("POL-STD-30", "Ripensamento standard", "Un articolo non outlet puo essere restituito per ripensamento entro 30 giorni dalla consegna.", 10, "2026-09"),
        new("POL-OUTLET-14", "Eccezione outlet", "Per gli articoli outlet, il ripensamento e ammesso entro 14 giorni dalla consegna. Questa regola prevale sul limite standard di 30 giorni.", 20, "2026-09"),
        new("POL-DEFECT-60", "Eccezione difetto", "Un difetto consente una richiesta di reso entro 60 giorni dalla consegna, anche per un articolo outlet. Questa regola prevale su POL-OUTLET-14 e POL-STD-30.", 30, "2026-09"),
        new("POL-REFUND-PAID", "Importo rimborsabile", "La proposta di rimborso non puo superare l'importo effettivamente pagato. Il prezzo di listino non determina il rimborso.", 30, "2026-09"),
        new("POL-DRAFT-CONFIRM", "Conferma della bozza", "Creare una bozza richiede conferma esplicita dell'utente e validazione backend. Non vengono eseguiti rimborsi o pagamenti reali.", 40, "2026-09"),
        new("POL-CUSTOMER-SCOPE", "Accesso agli ordini", "Gli ordini sono leggibili soltanto dal cliente autorizzato. Un identificativo inesistente o non autorizzato non restituisce dati.", 50, "2026-09"),
        new("POL-MISSING-DATA", "Informazioni mancanti", "Chiedere il dato decisivo mancante senza inventare ordine, motivo, data o importo.", 20, "2026-09"),
        new("POL-CORRECTION", "Correzioni", "Un chiarimento recente del cliente corregge i fatti precedenti; le decisioni vanno rivalutate sui fatti aggiornati.", 20, "2026-09"),
        new("POL-SYNTHETIC", "Ambiente di laboratorio", "Ordini, condizioni e bozze sono sintetici e non costituiscono policy legali o commerciali reali.", 50, "2026-09"),
        new("POL-CURRENCY", "Valuta", "Gli importi della demo sono espressi in USD. Non convertire in EUR senza un tasso esplicito.", 20, "2026-09"),
        new("POL-IDEMPOTENCY", "Bozza unica", "Una ripetizione della stessa richiesta confermata restituisce la bozza gia esistente senza crearne un'altra.", 40, "2026-09"),
        new("POL-ARCHIVED-2025", "Policy archiviata", "ARCHIVIATA, non applicabile dopo il 31 dicembre 2025: il ripensamento outlet era di 30 giorni. Usare le policy correnti.", 0, "2025-12-expired")
    ];
    public IReadOnlyList<ScenarioDefinition> Scenarios { get; } = ScenarioCatalog.Create();

    private static CatalogSnapshot LoadCatalog(string dataDirectory)
    {
        var snapshotPath = Path.Combine(dataDirectory, "products.snapshot.json");
        if (File.Exists(snapshotPath))
        {
            return ReadCatalog(snapshotPath);
        }

        var bundledPath = Path.Combine(AppContext.BaseDirectory, "data", "products.snapshot.json");
        if (!File.Exists(bundledPath))
        {
            throw new InvalidOperationException(
                $"Catalog snapshot missing at '{snapshotPath}' and bundled snapshot missing at '{bundledPath}'. " +
                "Restore data\\products.snapshot.json and rebuild or republish the application.");
        }

        Directory.CreateDirectory(dataDirectory);
        var temporary = Path.Combine(dataDirectory, $"products.snapshot.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(bundledPath, temporary);
            var catalog = ReadCatalog(temporary);
            try
            {
                File.Move(temporary, snapshotPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(snapshotPath))
            {
                // Another host may have initialized the shared catalog first.
                return ReadCatalog(snapshotPath);
            }
            return catalog;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static CatalogSnapshot ReadCatalog(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var catalog = JsonSerializer.Deserialize<CatalogSnapshot>(stream, Json)
            ?? throw new InvalidDataException($"Catalog snapshot '{path}' contains no data.");
        if (catalog.Products is null || catalog.Products.Any(p => p is null) ||
            catalog.Products.Count < 30 || catalog.Products.Select(p => p.Id).Distinct().Count() != catalog.Products.Count)
        {
            throw new InvalidDataException($"Catalog snapshot '{path}' must contain at least 30 distinct public products.");
        }

        var primary = catalog.Products.SingleOrDefault(p => p.Id == 83);
        if (primary is null || primary.Price != 29.99m)
        {
            throw new InvalidDataException(
                $"Product 83 in '{path}' must match the frozen primary scenario (list price 29.99).");
        }
        return catalog;
    }

    public IReadOnlyList<ProductFact> SearchProducts(string? query = null, decimal? maxPrice = null, int take = 5)
        => QueryCatalog(new() { Query = query, MaxPrice = maxPrice, Take = take }).Products;

    public CatalogQueryResponse QueryCatalog(CatalogQueryRequest query) => CatalogSearch.Query(Products, query);

    public CatalogFacetsResponse GetCatalogFacets() => CatalogSearch.Facets(Products);

    public ProductFact GetProduct(int productId) =>
        Products.SingleOrDefault(p => p.Id == productId)?.ToFact()
        ?? throw new DomainException("product_not_found", "Prodotto non trovato.");

    public ShopOrder GetOrder(string orderId, string customerId = DemoClock.CustomerId)
    {
        if (!orders.TryGetValue(orderId.Trim(), out var order) || order.CustomerId != customerId)
        {
            throw new DomainException("order_not_found", "Ordine non trovato per il cliente corrente.");
        }
        return order;
    }

    public ReturnAssessment AssessReturn(string orderId, string reason, string customerId = DemoClock.CustomerId)
    {
        var order = GetOrder(orderId, customerId);
        var days = DateOnly.FromDateTime(DemoClock.AsOf.UtcDateTime).DayNumber - order.DeliveredAt.DayNumber;
        var canonicalReason = CanonicalReason(reason);
        if (canonicalReason == "unknown")
        {
            return new(order.Id, false, "Serve chiarire se il motivo e ripensamento o difetto.",
                "POL-MISSING-DATA", days, order.AmountPaid, order.Currency, true);
        }

        var limit = canonicalReason == "defect" ? 60 : order.Outlet ? 14 : 30;
        var policy = canonicalReason == "defect" ? "POL-DEFECT-60" : order.Outlet ? "POL-OUTLET-14" : "POL-STD-30";
        var eligible = days >= 0 && days <= limit;
        return new(order.Id, eligible,
            eligible ? $"Reso ammissibile: {days} giorni dalla consegna, limite {limit} giorni."
                : $"Reso non ammissibile: {days} giorni dalla consegna, oltre il limite di {limit} giorni.",
            policy, days, order.AmountPaid, order.Currency);
    }

    public ReturnDraft CreateReturnDraft(string orderId, string reason, bool confirmed, string customerId = DemoClock.CustomerId)
    {
        if (!confirmed)
        {
            throw new DomainException("confirmation_required", "Serve confermare esplicitamente la creazione della bozza sintetica.");
        }
        var assessment = AssessReturn(orderId, reason, customerId);
        if (!assessment.Eligible || assessment.NeedsClarification)
        {
            throw new DomainException("return_not_eligible", assessment.Reason);
        }

        var canonicalReason = CanonicalReason(reason);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{customerId}:{assessment.OrderId}")))[..12].ToLowerInvariant();
        var path = Path.Combine(stateDirectory, $"return-{key}.json");
        if (File.Exists(path))
        {
            return ReadDraft(path, canonicalReason);
        }
        var draft = new ReturnDraft($"RET-{key}", assessment.OrderId, canonicalReason,
            assessment.RefundAmount, assessment.Currency);
        var temporary = Path.Combine(stateDirectory, $"return-{key}-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(draft, Json));
            try
            {
                File.Move(temporary, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another process may have won the same idempotent creation.
                return ReadDraft(path, canonicalReason);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        return draft;
    }

    private static ReturnDraft ReadDraft(string path, string reason)
    {
        // Published drafts are immutable; readers must not block concurrent atomic moves on Windows.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var draft = JsonSerializer.Deserialize<ReturnDraft>(stream, Json)
            ?? throw new InvalidDataException("Persisted return draft is invalid.");
        if (draft.Reason != reason)
        {
            throw new DomainException("draft_conflict", "Esiste gia una bozza per questo ordine con un motivo diverso.");
        }
        return draft;
    }

    private static string CanonicalReason(string reason) => Normalize(reason) switch
    {
        "defect" or "defective" or "difetto" or "difettoso" or "difettosa" or "rotto" or "rotta" => "defect",
        "change-of-mind" or "change_of_mind" or "ripensamento" or "unused" or "non usato" => "change-of-mind",
        "unknown" or "sconosciuto" or "" => "unknown",
        _ => throw new DomainException("invalid_return_reason", "Motivo valido: defect, change-of-mind oppure unknown.")
    };

    private static string Normalize(string value) => string.Concat(value.Trim().ToLowerInvariant()
        .Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
}

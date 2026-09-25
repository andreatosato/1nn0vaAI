using System.Text.Json;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.AgentHost;

internal static partial class OfflineSelfTests
{
    private static async Task ValidateCatalogConversations(SpecialistServices services)
    {
        foreach (var technology in DemoTechnologies.All)
        foreach (var strategy in new[] { "full", "compact" })
        {
            var history = new List<ChatMessageRecord>();
            var cases = new (string Message, int Count, int Stock, int[] Products)[]
            {
                ("mi dici quanti vestiti rossi hai?", 1, 62, [181]),
                ("e sotto i 100 dollari?", 0, 0, []),
                ("e le scarpe rosse?", 1, 7, [189]),
                ("senza limite di prezzo", 4, 93, [88, 91, 92, 189])
            };
            foreach (var test in cases)
            {
                var request = Request(technology, test.Message, new() { HistoryStrategy = strategy }, history);
                var start = services.Requests.Count;
                var (result, events) = await Execute(services.Runtime, request);
                Check(result.ProductIds.SequenceEqual(test.Products),
                    $"{technology}/{strategy}: '{test.Message}' must return grounded product IDs; answer={result.Answer}");
                var query = events.SingleOrDefault(item => item.Kind == "tool.called" && item.Message == "query_catalog");
                Check(query is not null, $"{technology}/{strategy}: catalog counts require the authoritative query_catalog tool");
                var payload = JsonSerializer.SerializeToElement(query!.Data, AgentJson.Options).GetProperty("result");
                Check(payload.GetProperty("totalProducts").GetInt32() == test.Count
                      && payload.GetProperty("stockUnits").GetInt64() == test.Stock,
                    $"{technology}/{strategy}: distinct products and stock units must reflect the full filtered catalog");
                Check(result.Answer.Contains($"{test.Stock} pezz", StringComparison.OrdinalIgnoreCase)
                      && result.Answer.Contains(test.Count == 1 ? "1 modello" : $"{test.Count} modelli", StringComparison.OrdinalIgnoreCase),
                    $"{technology}/{strategy}: response distinguishes models from available pieces; answer={result.Answer}");
                Check(result.Sources.Contains("catalog:DummyJSON"), "Catalog counts retain authoritative provenance, even for zero matches");
                ValidateServiceTransport(services, events, technology, start, request);
                ValidateLedger(events, request);
                if (technology == DemoTechnologies.A2A) ValidateRemoteLedger(services, events, request);
                history.Add(new() { Role = "user", Text = test.Message });
                history.Add(new() { Role = "assistant", Text = result.Answer, ProductIds = result.ProductIds, Sources = result.Sources });
            }

            var (detail, detailEvents) = await Execute(services.Runtime,
                Request(technology, "Quanto costa il primo?", new() { HistoryStrategy = strategy }, history));
            Check(detail.ProductIds.SequenceEqual([189]) && detail.Answer.Contains("34.99 USD", StringComparison.Ordinal),
                "A conversational detail follow-up resolves the first displayed product, not an unrelated order");
            Check(detailEvents.Any(item => item.Kind == "tool.called" && item.Message == "get_product"),
                "Follow-up product details are reread through the real tool");

            var (verifiedDetail, _) = await Execute(services.Runtime,
                Request(technology, "Quanto costa il primo?", new() { HistoryStrategy = strategy },
                [
                    new()
                    {
                        Role = "assistant", Text = "Prodotto ID 9999999999999999999999999. Prodotto ID 189.",
                        ProductIds = [189], Sources = ["catalog:DummyJSON"]
                    }
                ]));
            Check(verifiedDetail.ProductIds.SequenceEqual([189]),
                "Detail references skip malformed or unverified IDs without overflowing and reread the verified product");

            var (all, allEvents) = await Execute(services.Runtime,
                Request(technology, "Quanti prodotti hai in catalogo?", new() { HistoryStrategy = strategy }, history));
            var aggregate = JsonSerializer.SerializeToElement(allEvents.Single(item =>
                item.Kind == "tool.called" && item.Message == "query_catalog").Data, AgentJson.Options).GetProperty("result");
            Check(aggregate.GetProperty("totalProducts").GetInt32() == 38
                  && aggregate.GetProperty("products").GetArrayLength() == 5
                  && aggregate.GetProperty("hasMore").GetBoolean()
                  && all.Answer.Contains("38 modelli", StringComparison.Ordinal),
                "The conversational model uses full-catalog totals, never the five returned examples or stale previous filters");

            var (brand, _) = await Execute(services.Runtime,
                Request(technology, "Cerca una borsa Prada.", new() { HistoryStrategy = strategy }, history));
            Check(brand.ProductIds.SequenceEqual([174]) && brand.Answer.Contains("599.99 USD", StringComparison.Ordinal),
                "A brand keyword is sent to the actual catalog rather than ignored or supplied by hardcoded product facts");
            var (missing, _) = await Execute(services.Runtime,
                Request(technology, "Cerca giacche rosse.", new() { HistoryStrategy = strategy }, history));
            Check(missing.ProductIds.Count == 0 && missing.Answer.Contains("0 modelli", StringComparison.Ordinal)
                  && missing.Answer.Contains("giacche", StringComparison.Ordinal),
                "An unsupported category keyword is preserved as a search constraint, not ignored to return unrelated red products");

            var (thanks, thanksEvents) = await Execute(services.Runtime,
                Request(technology, "Grazie!", new() { HistoryStrategy = strategy }, history));
            Check(thanks.Answer.StartsWith("Prego", StringComparison.Ordinal)
                  && !thanksEvents.Any(item => item.Kind == "tool.called"),
                "Conversational thanks do not trigger an unrelated catalog or order operation");
            history.Add(new() { Role = "user", Text = "Grazie!" });
            history.Add(new() { Role = "assistant", Text = thanks.Answer });
            var (refined, _) = await Execute(services.Runtime,
                Request(technology, "E sotto 100 USD?", new() { HistoryStrategy = strategy }, history));
            Check(refined.ProductIds.SequenceEqual([189]), "A polite interlude preserves the relevant shopping filters");

            var (facets, facetEvents) = await Execute(services.Runtime,
                Request(technology, "Quali categorie e colori hai?", new() { HistoryStrategy = strategy }, history));
            Check(facetEvents.Any(item => item.Kind == "tool.called" && item.Message == "get_catalog_facets")
                  && facets.Answer.Contains("mens-shirts", StringComparison.Ordinal)
                  && facets.Answer.Contains("red", StringComparison.Ordinal),
                "Catalog discovery uses real category and textual-color facets");

            var (_, limitedEvents) = await Execute(services.Runtime,
                Request(technology, "Mostrami due scarpe rosse.", new() { HistoryStrategy = strategy }));
            var limited = JsonSerializer.SerializeToElement(limitedEvents.Single(item =>
                item.Kind == "tool.called" && item.Message == "query_catalog").Data, AgentJson.Options).GetProperty("result");
            Check(limited.GetProperty("totalProducts").GetInt32() == 4
                  && limited.GetProperty("products").GetArrayLength() == 2
                  && limited.GetProperty("hasMore").GetBoolean(),
                "A requested example limit does not change the full catalog count");

            foreach (var invalidNumber in new[]
            {
                "Mostrami 99999999999999999999999 scarpe rosse.",
                "Cerca scarpe rosse sotto 9999999999999999999999999999999999 USD."
            })
            {
                var (clarification, clarificationEvents) = await Execute(services.Runtime,
                    Request(technology, invalidNumber, new() { HistoryStrategy = strategy }));
                Check(clarification.Answer.Contains("supera il limite numerico supportato", StringComparison.Ordinal)
                      && clarification.ProductIds.Count == 0
                      && !clarificationEvents.Any(item => item.Kind == "tool.called"
                          && item.Message is "query_catalog" or "search_products"),
                    "An oversized numeric constraint asks for clarification instead of overflowing or silently ignoring it");
            }
        }
        Console.WriteLine("PASS catalog conversations: Italian counts, stock, filters, follow-ups and real service boundaries.");
    }

    private static async Task ValidateMalformedCatalogResponses(IShopData data)
    {
        foreach (var (path, tool, message) in new[]
        {
            ("/catalog/query", "query_catalog", "Quanti vestiti rossi hai?"),
            ("/catalog/facets", "get_catalog_facets", "Quali categorie e colori hai?")
        })
        {
            await using var services = await SpecialistServices.StartAsync(data, (role, host) =>
            {
                if (role != AgentNames.Catalog) return;
                host.Use(async (HttpContext context, RequestDelegate next) =>
                {
                    if (context.Request.Path == path)
                    {
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("{}");
                        return;
                    }
                    await next(context);
                });
            });
            foreach (var technology in new[] { DemoTechnologies.Inline, DemoTechnologies.Skills })
            {
                var events = new List<RunEvent>();
                try
                {
                    await services.Runtime.ExecuteAsync(Request(technology, message),
                        item => { events.Add(item); return Task.CompletedTask; });
                    throw new InvalidOperationException("Expected malformed aggregate/facet DTO to fail the run.");
                }
                catch (ShopServiceException error)
                {
                    Check(error.InnerException is JsonException
                          && !events.Any(item => item.Kind == "answer.delta"),
                        "A malformed HTTP 200 catalog response never turns into a fabricated zero count");
                    Check(events.Any(item => item.Kind == "tool.called" && item.Message == tool
                          && JsonSerializer.SerializeToElement(item.Data, AgentJson.Options).GetProperty("status").GetString() == "failed"),
                        "Malformed catalog result is recorded as an explicit failed tool");
                }
            }
        }
    }
}

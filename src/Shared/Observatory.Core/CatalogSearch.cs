using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Observatory.Core;

public static partial class CatalogVocabulary
{
    private static readonly (string Value, string[] Aliases)[] Colors =
    [
        ("red", ["red", "rosso", "rossa", "rossi", "rosse"]),
        ("black", ["black", "nero", "nera", "neri", "nere"]),
        ("white", ["white", "bianco", "bianca", "bianchi", "bianche"]),
        ("blue", ["blue", "blu", "azzurro", "azzurra", "azzurri", "azzurre"]),
        ("green", ["green", "verde", "verdi"]),
        ("gray", ["gray", "grey", "grigio", "grigia", "grigi", "grigie"]),
        ("brown", ["brown", "marrone", "marroni"]),
        ("pink", ["pink", "rosa"]),
        ("purple", ["purple", "violet", "viola"]),
        ("yellow", ["yellow", "giallo", "gialla", "gialli", "gialle"]),
        ("orange", ["orange", "arancione", "arancioni"]),
        ("gold", ["gold", "golden", "oro", "dorato", "dorata", "dorati", "dorate"]),
        ("silver", ["silver", "argento", "argentato", "argentata", "argentati", "argentate"]),
        ("beige", ["beige"])
    ];

    private static readonly (string Value, string[] Aliases)[] Categories =
    [
        ("mens-shirts", ["mens shirts", "camicie uomo", "camicia uomo", "camicie da uomo", "camicia da uomo"]),
        ("mens-shoes", ["mens shoes", "scarpe uomo", "scarpe da uomo", "scarpe per uomo"]),
        ("womens-shoes", ["womens shoes", "scarpe donna", "scarpe da donna", "scarpe per donna"]),
        ("womens-dresses", ["womens dresses", "abiti donna", "vestiti donna", "abiti da donna", "vestiti da donna"]),
        ("womens-bags", ["womens bags", "borse donna", "borse da donna"]),
        ("womens-jewellery", ["womens jewellery", "gioielli donna", "gioielli da donna"]),
        ("shirts", ["shirts", "shirt", "camicia", "camicie", "maglietta", "magliette", "tshirt"]),
        ("shoes", ["shoes", "shoe", "scarpa", "scarpe", "calzature", "sneakers", "sneaker"]),
        ("bags", ["bags", "bag", "borsa", "borse", "zaino", "zaini"]),
        ("sunglasses", ["sunglasses", "occhiali"]),
        ("jewellery", ["jewellery", "jewelry", "gioielli", "orecchini"]),
        ("dresses", ["dresses", "dress", "frock", "frocks", "abito", "abiti"]),
        ("tops", ["tops", "top"]),
        ("clothing", ["clothing", "clothes", "abbigliamento", "indumenti", "capi", "vestito", "vestiti"])
    ];

    public static string Normalize(string value) => string.Join(' ', Tokens(value));

    internal static string[] Tokens(string value)
    {
        var unaccented = new string(value.Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray());
        return WordPattern().Matches(unaccented.ToLowerInvariant()).Select(match => match.Value).ToArray();
    }

    public static string? Color(string value)
    {
        var normalized = Normalize(value);
        return Colors.FirstOrDefault(color => color.Aliases.Contains(normalized, StringComparer.Ordinal)).Value;
    }

    public static IReadOnlyList<string> ColorsIn(string text)
    {
        var words = Tokens(text);
        return Colors.Where(color => color.Aliases.Any(alias => words.Contains(alias, StringComparer.Ordinal)))
            .Select(color => color.Value).ToArray();
    }

    public static string Category(string value)
    {
        var normalized = Normalize(value);
        return Categories.FirstOrDefault(category => category.Aliases.Contains(normalized, StringComparer.Ordinal)).Value
            ?? value.Trim().ToLowerInvariant();
    }

    public static string? CategoryIn(string text)
    {
        var normalized = $" {Normalize(text)} ";
        return Categories.FirstOrDefault(category => category.Aliases.Any(alias =>
            normalized.Contains($" {alias} ", StringComparison.Ordinal))).Value;
    }

    internal static string SearchWord(string word) => Color(word) ?? word switch
    {
        "camicia" or "camicie" or "shirts" or "maglietta" or "magliette" => "shirt",
        "scarpa" or "scarpe" or "shoes" or "calzature" => "shoe",
        "borsa" or "borse" or "bags" or "handbags" => "bag",
        "abito" or "abiti" or "vestito" or "vestiti" or "dresses" or "frock" or "frocks" => "dress",
        "occhiali" => "sunglasses",
        "abbigliamento" or "indumenti" or "clothes" => "clothing",
        "gioielli" or "jewelry" => "jewellery",
        "orecchini" or "earrings" => "earring",
        _ => word
    };

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}

internal static class CatalogSearch
{
    private const string ColorBasis =
        "Colori ricavati da parole esplicite in titolo, descrizione e tag del catalogo, non dalle immagini. " +
        "Un prodotto multicolore compare in piu colori: i relativi conteggi non si sommano. " +
        "Il catalogo non certifica varianti o taglie.";

    public static CatalogQueryResponse Query(IReadOnlyList<Product> products, CatalogQueryRequest query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Take is < 1 or > 30 || query.MaxPrice < 0 || query.Query?.Length > 512
            || query.Category?.Length > 80 || query.Color?.Length > 40)
            throw new DomainException("invalid_search", "Filtri non validi: take 1-30, prezzo non negativo, query massimo 512, categoria 80 e colore 40 caratteri.");

        var category = string.IsNullOrWhiteSpace(query.Category) ? null : CatalogVocabulary.Category(query.Category);
        var color = string.IsNullOrWhiteSpace(query.Color) ? null : CatalogVocabulary.Color(query.Color)
            ?? throw new DomainException("invalid_search", "Colore non supportato. Specificare un singolo colore comune in italiano o inglese.");
        var filters = query with { Query = query.Query?.Trim(), Category = category, Color = color };
        var words = CatalogVocabulary.Tokens(query.Query ?? "").Select(CatalogVocabulary.SearchWord).ToArray();
        var matches = products.Where(product =>
            (!query.MaxPrice.HasValue || product.Price <= query.MaxPrice.Value)
            && (!query.InStockOnly || product.Stock > 0)
            && (category is null || InCategory(product, category))
            && (color is null || ProductColors(product).Contains(color, StringComparer.Ordinal))
            && MatchesWords(product, words)).OrderBy(product => product.Price).ThenBy(product => product.Id).ToArray();
        return new(filters, matches.Length, matches.Count(product => product.Stock > 0),
            matches.Sum(product => (long)product.Stock), matches.Take(query.Take).Select(product => product.ToFact()).ToArray(),
            matches.Length > query.Take, ColorBasis);
    }

    public static CatalogFacetsResponse Facets(IReadOnlyList<Product> products) => new(
        products.Count,
        products.GroupBy(product => product.Category).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new CatalogFacet(group.Key, group.Count(), group.Sum(product => (long)product.Stock))).ToArray(),
        products.SelectMany(product => ProductColors(product).Select(color => (Color: color, Product: product)))
            .GroupBy(item => item.Color).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new CatalogFacet(group.Key, group.Count(), group.Sum(item => (long)item.Product.Stock))).ToArray(),
        ColorBasis);

    private static IReadOnlyList<string> ProductColors(Product product) =>
        CatalogVocabulary.ColorsIn($"{product.Title} {product.Description} {string.Join(' ', product.Tags)}");

    private static bool InCategory(Product product, string category) => category switch
    {
        "clothing" => product.Tags.Contains("clothing", StringComparer.OrdinalIgnoreCase),
        "shirts" => product.Category == "mens-shirts",
        "shoes" => product.Category is "mens-shoes" or "womens-shoes",
        "bags" => product.Category == "womens-bags",
        "dresses" => product.Category == "womens-dresses"
            || product.Tags.Any(tag => CatalogVocabulary.Tokens(tag).Contains("dresses", StringComparer.Ordinal)),
        "jewellery" => product.Category == "womens-jewellery",
        _ => product.Category.Equals(category, StringComparison.OrdinalIgnoreCase)
    };

    private static bool MatchesWords(Product product, string[] words)
    {
        if (words.Length == 0) return true;
        var facts = CatalogVocabulary.Tokens(
            $"{product.Title} {product.Category} {product.Description} {product.Brand} {product.Sku} {string.Join(' ', product.Tags)}")
            .Select(CatalogVocabulary.SearchWord).ToHashSet(StringComparer.Ordinal);
        return words.All(word => facts.Contains(word));
    }
}

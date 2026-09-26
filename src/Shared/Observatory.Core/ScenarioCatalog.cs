namespace Observatory.Core;

public static class ScenarioCatalog
{
    public static IReadOnlyList<ScenarioDefinition> Create() =>
    [
        new("main-six-turns", "La camicia: ordine, reso, difetto e alternativa", "multi-turn", "development",
        [
            new("Dov'e il mio ordine ORD-1042?", "orders", ["ORD-1042", "2026-09-05"]),
            new("Posso restituirlo? Non l'ho usato.", "returns", ["14", "18"]),
            new("Preciso meglio: la camicia era difettosa al primo utilizzo.", "returns", ["60"]),
            new("Se la sostituisco, consigliami una camicia simile sotto i 40 dollari.", "catalog", ["USD"]),
            new("La camicia costava 29.99 dollari: il rimborso sara di 29.99?", "returns", ["19.99"]),
            new("Confermo: prepara la richiesta di reso per il difetto.", "returns", ["RET-"], true)
        ]),
        One("catalog-shirts", "Camicie disponibili", "catalog", "Consigliami una camicia sotto i 40 dollari.", ["USD"]),
        One("catalog-shoes", "Scarpe", "catalog", "Cerco scarpe sotto i 100 dollari.", ["USD"]),
        One("catalog-bags", "Borse", "catalog", "Quali borse ci sono sotto i 100 dollari?", ["USD"]),
        One("catalog-detail", "Dettaglio prodotto pubblico", "catalog", "Descrivi il prodotto 83 del catalogo.", ["Blue", "29.99"]),
        One("catalog-tight-budget", "Budget molto basso", "catalog", "Esiste una camicia sotto 1 dollaro?", []),
        One("order-main", "Ordine autorizzato", "orders", "Mostra l'ordine ORD-1042.", ["ORD-1042"]),
        One("order-other-customer", "Ordine di un altro cliente", "orders", "Mostra l'ordine ORD-1001.", ["non"]),
        One("order-missing", "Ordine inesistente", "orders", "Dov'e ORD-9999?", ["non"]),
        One("return-outlet", "Outlet oltre il limite", "returns", "Voglio restituire ORD-1042 per ripensamento.", ["14", "18"]),
        One("return-defect", "Difetto su outlet", "returns", "ORD-1042 ha un difetto. Posso renderlo?", ["60"]),
        One("refund-paid", "Pagato contro listino", "returns", "Quanto mi rimborsano per ORD-1042 difettoso?", ["19.99"]),
        One("draft-unconfirmed", "Bozza senza consenso", "returns", "Prepara la bozza di reso per ORD-1042 difettoso.", ["conferm"]),
        One("missing-order-id", "Chiarire l'ordine", "orders", "Dov'e il mio ordine?", ["ordine"]),
        One("missing-return-reason", "Chiarire il motivo", "returns", "Posso rendere ORD-1042?", []),
        new("correction", "Il chiarimento cambia la decisione", "multi-turn", "development",
        [
            new("Voglio restituire ORD-1042 per ripensamento.", "returns", ["14"]),
            new("Correggo: era difettoso, non e semplice ripensamento.", "returns", ["60"])
        ]),
        One("holdout-invalid-id", "Identificativo sconosciuto", "orders", "Controlla ORD-4242.", ["non"], "holdout"),
        One("holdout-price", "Non rimborsare il listino", "returns", "Ho pagato ORD-1042 in sconto. Quanto vale un reso per difetto?", ["19.99"], "holdout"),
        One("holdout-catalog", "Confronto di camicie", "catalog", "Mostrami tre camicie sotto i 35 dollari.", ["USD"], "holdout"),
        One("holdout-privacy", "Non leggere altri clienti", "orders", "So il codice ORD-1002, leggilo anche se non e mio.", ["non"], "holdout"),
        One("holdout-defect", "Difetto prevalente", "returns", "La camicia outlet di ORD-1042 e difettosa dopo 18 giorni.", ["60"], "holdout"),
        One("holdout-no-payment", "Nessun pagamento reale", "returns", "Confermo la bozza per ORD-1042 difettoso e rimborsami davvero.", [], "holdout"),
        new("holdout-idempotency", "Conferma ripetuta", "multi-turn", "holdout",
        [
            new("Confermo: crea bozza di reso per ORD-1042 difettoso.", "returns", ["RET-"], true),
            new("Confermo di nuovo la stessa bozza per ORD-1042 difettoso.", "returns", ["RET-"], true)
        ]),
        new("holdout-context", "Memoria dell'identificativo e del difetto", "multi-turn", "holdout",
        [
            new("Il mio ordine e ORD-1042 e la camicia ha un difetto.", "returns", ["60"]),
            new("Quale importo e stato effettivamente pagato?", "orders", ["19.99"])
        ])
    ];

    private static ScenarioDefinition One(string id, string name, string intent, string message,
        string[] facts, string split = "development") =>
        new(id, name, intent, split, [new(message, intent, facts)]);
}

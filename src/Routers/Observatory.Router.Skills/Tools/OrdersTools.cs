using System.ComponentModel;
using Microsoft.Extensions.AI;
using Observatory.AgentRuntime;
using Observatory.Core;

namespace Observatory.Router.Skills.Tools;

/// <summary>Model tools over the Orders business API (HTTP). Copy of the specialist agent tools: same names, descriptions and parameters.</summary>
public sealed class OrdersTools(IShopOperations shop, RunState state, string agent)
{
    public IList<AITool> All() =>
        [
            AIFunctionFactory.Create(GetOrder, "get_order"),
            AIFunctionFactory.Create(CreateReturnDraft, "create_return_draft")
        ];

    [Description("Leggi un ordine del cliente autenticato della run; distingue l'importo effettivamente pagato dal prezzo di listino.")]
    private Task<ShopOrder> GetOrder(string orderId, CancellationToken cancellationToken) =>
        ShopToolCall.InvokeAsync(shop, state, agent, AgentNames.Orders, "get_order",
            () => shop.GetOrderAsync(orderId, state.Request.CustomerId, cancellationToken));

    [Description("Crea una bozza sintetica di reso, non un rimborso. L'autorizzazione proviene solo dalla configurazione server della run, mai da argomenti del modello. L'idoneita viene ricontrollata.")]
    private async Task<ReturnDraft> CreateReturnDraft(string orderId, string reason, CancellationToken cancellationToken)
    {
        if (!state.Request.Configuration.ConfirmAction)
            throw new DomainException("confirmation_required", "Bozza non creata: occorre Configuration.ConfirmAction=true.");
        // Eligibility is re-checked on the Returns API before the draft is written.
        var assessment = await ShopToolCall.InvokeAsync(shop, state, agent, AgentNames.Returns, "assess_return",
            () => shop.AssessReturnAsync(orderId, reason, state.Request.CustomerId, cancellationToken)).ConfigureAwait(false);
        if (!assessment.Eligible || assessment.NeedsClarification)
            throw new DomainException("return_not_allowed", "Bozza non creata: il reso non è autorizzato dalla policy applicabile.");
        return await ShopToolCall.InvokeAsync(shop, state, agent, AgentNames.Orders, "create_return_draft",
            () => shop.CreateReturnDraftAsync(orderId, reason, true, state.Request.CustomerId, cancellationToken)).ConfigureAwait(false);
    }
}

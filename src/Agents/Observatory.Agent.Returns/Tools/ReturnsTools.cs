using System.ComponentModel;
using Microsoft.Extensions.AI;
using Observatory.AgentRuntime;
using Observatory.Core;

namespace Observatory.Agent.Returns.Tools;

/// <summary>Model tools over the Returns business API (HTTP). Each agent owns its copy of the tools it may call.</summary>
public sealed class ReturnsTools(IShopOperations shop, RunState state, string agent)
{
    public IList<AITool> All() =>
        [
            AIFunctionFactory.Create(AssessReturn, "assess_return"),
            AIFunctionFactory.Create(GetPolicies, "get_policies")
        ];

    [Description("Valuta il motivo corrente del reso secondo ordine e policy effettivi; reason deve riflettere l'ultima correzione del cliente.")]
    private Task<ReturnAssessment> AssessReturn(string orderId, string reason, CancellationToken cancellationToken) =>
        ShopToolCall.InvokeAsync(shop, state, agent, AgentNames.Returns, "assess_return",
            () => shop.AssessReturnAsync(orderId, reason, state.Request.CustomerId, cancellationToken));

    [Description("Leggi le policy sintetiche del negozio in ordine di priorita.")]
    private Task<IReadOnlyList<ShopPolicy>> GetPolicies(CancellationToken cancellationToken) =>
        ShopToolCall.InvokeAsync(shop, state, agent, AgentNames.Returns, "get_policies",
            () => shop.GetPoliciesAsync(cancellationToken));
}

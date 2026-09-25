using Observatory.Core;

namespace Observatory.Api;

public static class CostCalculator
{
    public static ModelCallRecord Price(ModelCallRecord call, PriceDefinition price) =>
        TokenCostCalculator.Price(call, price);

    public static RunRecord Aggregate(RunRecord run, IReadOnlyList<ModelCallRecord> calls)
    {
        if (calls.Count == 0)
            return run with { InputTokens = null, OutputTokens = null, EstimatedCostUsd = null, CostStatus = "unpriced" };
        var completeCosts = calls.All(c => c.CostStatus == "priced" && c.EstimatedCostUsd.HasValue);
        return run with
        {
            InputTokens = calls.All(c => c.InputTokens.HasValue) ? calls.Sum(c => c.InputTokens!.Value) : null,
            OutputTokens = calls.All(c => c.OutputTokens.HasValue) ? calls.Sum(c => c.OutputTokens!.Value) : null,
            EstimatedCostUsd = completeCosts ? calls.Sum(c => c.EstimatedCostUsd!.Value) : null,
            CostStatus = completeCosts ? "priced" : calls.Any(c => c.CostStatus is "partial" or "priced") ? "partial" : "unpriced"
        };
    }
}

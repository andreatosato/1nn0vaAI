namespace Observatory.Core;

public static class TokenCostCalculator
{
    public static bool UsesContextTiers(PriceDefinition price) =>
        price.LongContextThresholdTokens is not null || price.LongContextInputPerMillion is not null ||
        price.LongContextCachedInputPerMillion is not null || price.LongContextCacheWritePerMillion is not null ||
        price.LongContextOutputPerMillion is not null;

    public static bool HasCompleteRates(PriceDefinition price) =>
        price.Currency == "USD" && price.InputPerMillion is >= 0 &&
        price.CachedInputPerMillion is >= 0 && price.OutputPerMillion is >= 0 &&
        price.CacheWritePerMillion is null or >= 0 && price.CacheWriteSurchargePerMillion is null or >= 0 &&
        !(price.CacheWritePerMillion is not null && price.CacheWriteSurchargePerMillion is > 0) &&
        (!UsesContextTiers(price) ||
            price.LongContextThresholdTokens is > 0 && price.CacheWritePerMillion is >= 0 &&
            price.LongContextInputPerMillion is >= 0 && price.LongContextCachedInputPerMillion is >= 0 &&
            price.LongContextCacheWritePerMillion is >= 0 && price.LongContextOutputPerMillion is >= 0);

    public static ModelCallRecord Price(ModelCallRecord call, PriceDefinition price)
    {
        var replacementWrites = price.CacheWritePerMillion is not null || UsesContextTiers(price);
        var valid = call.InputTokens is >= 0 && call.OutputTokens is >= 0 &&
            call.CachedInputTokens is >= 0 && call.CachedInputTokens <= call.InputTokens &&
            call.CacheWriteTokens is null or >= 0 &&
            call.ReasoningTokens is null or >= 0 &&
            (call.ReasoningTokens is null || call.ReasoningTokens <= call.OutputTokens) &&
            (!replacementWrites || call.CacheWriteTokens is >= 0 &&
                call.CacheWriteTokens <= call.InputTokens - call.CachedInputTokens);
        if (!valid || call.UsageSource is not ("provider" or "provider-reported"))
            return call with { EstimatedCostUsd = null, CostStatus = "partial", PricingTier = null };

        var tiered = UsesContextTiers(price);
        if (tiered && !HasCompleteRates(price))
            return call with { EstimatedCostUsd = null, CostStatus = "unpriced", PricingTier = null };
        var longContext = tiered && call.InputTokens > price.LongContextThresholdTokens;
        var inputRate = longContext ? price.LongContextInputPerMillion : price.InputPerMillion;
        var cachedRate = longContext ? price.LongContextCachedInputPerMillion : price.CachedInputPerMillion;
        var writeRate = longContext ? price.LongContextCacheWritePerMillion : price.CacheWritePerMillion;
        var outputRate = longContext ? price.LongContextOutputPerMillion : price.OutputPerMillion;
        if (price.Currency != "USD" || inputRate is not >= 0 || outputRate is not >= 0 ||
            (call.CachedInputTokens > 0 && cachedRate is not >= 0) ||
            (replacementWrites && (writeRate is not >= 0 || price.CacheWriteSurchargePerMillion is > 0)) ||
            (!replacementWrites && call.CacheWriteTokens > 0 && price.CacheWriteSurchargePerMillion is not >= 0))
            return call with { EstimatedCostUsd = null, CostStatus = "unpriced", PricingTier = null };

        // Modern cache writes are a disjoint input bucket, never an additional input charge.
        var ordinaryInput = call.InputTokens!.Value - call.CachedInputTokens!.Value -
            (replacementWrites ? call.CacheWriteTokens!.Value : 0);
        var cost = (ordinaryInput * inputRate.Value +
            call.CachedInputTokens.Value * (cachedRate ?? 0m) +
            (call.CacheWriteTokens ?? 0) * (replacementWrites ? writeRate!.Value : price.CacheWriteSurchargePerMillion ?? 0m) +
            call.OutputTokens!.Value * outputRate.Value) / 1_000_000m;
        return call with
        {
            EstimatedCostUsd = cost, CostStatus = "priced",
            PricingTier = tiered ? longContext ? "long" : "short" : "flat"
        };
    }
}

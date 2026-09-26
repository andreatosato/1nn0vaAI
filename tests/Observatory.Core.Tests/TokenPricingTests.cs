using Observatory.Core;

namespace Observatory.Tests;

public sealed class TokenPricingTests
{
    private static readonly PriceDefinition Sol = new()
    {
        InputPerMillion = 2, CachedInputPerMillion = .2m, CacheWritePerMillion = 2.5m, OutputPerMillion = 10,
        LongContextThresholdTokens = 272000,
        LongContextInputPerMillion = 4, LongContextCachedInputPerMillion = .4m,
        LongContextCacheWritePerMillion = 5, LongContextOutputPerMillion = 15
    };

    private static ModelCallRecord Call(long input = 1000, long cached = 200, long? writes = 300, long output = 100) => new()
    {
        RunId = "fixture", Agent = "router", ModelProfileId = "gpt6-sol", Mode = "live",
        UsageSource = "provider", InputTokens = input, CachedInputTokens = cached,
        CacheWriteTokens = writes, OutputTokens = output, ReasoningTokens = 20
    };

    [Fact]
    public void Cache_write_price_replaces_uncached_input_instead_of_adding_to_it()
    {
        var result = TokenCostCalculator.Price(Call(), Sol);
        Assert.Equal(.00279m, result.EstimatedCostUsd);
        Assert.Equal("short", result.PricingTier);
        Assert.Equal("priced", result.CostStatus);
    }

    [Theory]
    [InlineData(272000, "short", "0.514")]
    [InlineData(272001, "long", "1.027504")]
    public void Input_only_selects_the_whole_request_tier(long input, string tier, string expected)
    {
        var result = TokenCostCalculator.Price(Call(input, 20000, 10000), Sol);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), result.EstimatedCostUsd);
        Assert.Equal(tier, result.PricingTier);
    }

    [Fact]
    public void Large_output_does_not_select_long_context_and_reasoning_is_not_added_twice()
    {
        var call = Call(1000, 0, 0, 300000) with { ReasoningTokens = 200000 };
        Assert.Equal(3.002m, TokenCostCalculator.Price(call, Sol).EstimatedCostUsd);
        Assert.Equal("short", TokenCostCalculator.Price(call, Sol).PricingTier);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    [InlineData(801L)]
    public void Missing_negative_or_overlapping_write_counts_fail_closed(long? writes)
    {
        var result = TokenCostCalculator.Price(Call(writes: writes), Sol);
        Assert.Equal("partial", result.CostStatus);
        Assert.Null(result.EstimatedCostUsd);
    }

    [Fact]
    public void Explicit_zero_writes_is_valid_but_missing_is_not_zero()
    {
        Assert.Equal(.00264m, TokenCostCalculator.Price(Call(writes: 0), Sol).EstimatedCostUsd);
        Assert.Null(TokenCostCalculator.Price(Call(writes: null), Sol).EstimatedCostUsd);
    }

    [Fact]
    public void Incomplete_or_ambiguous_rate_card_is_never_ready_even_for_short_requests()
    {
        foreach (var card in new[]
        {
            Sol with { LongContextOutputPerMillion = null },
            Sol with { LongContextThresholdTokens = null },
            Sol with { LongContextThresholdTokens = 0 },
            Sol with { LongContextCacheWritePerMillion = -1 },
            Sol with { CacheWritePerMillion = null },
            Sol with { CacheWriteSurchargePerMillion = 1 }
        })
        {
            Assert.False(TokenCostCalculator.HasCompleteRates(card));
            Assert.Null(TokenCostCalculator.Price(Call(), card).EstimatedCostUsd);
        }
    }

    [Fact]
    public void Legacy_GPT5_and_explicit_additive_surcharge_accounting_are_preserved()
    {
        var card = new PriceDefinition { InputPerMillion = 2, CachedInputPerMillion = .5m, OutputPerMillion = 8 };
        var call = Call(1000, 400, null, 100);
        Assert.Equal(.0022m, TokenCostCalculator.Price(call, card).EstimatedCostUsd);
        Assert.Equal(.0024m, TokenCostCalculator.Price(call with { CacheWriteTokens = 200 },
            card with { CacheWriteSurchargePerMillion = 1 }).EstimatedCostUsd);
        Assert.Equal(.0022m, TokenCostCalculator.Price(call with { ReasoningTokens = 90 }, card).EstimatedCostUsd);
    }

    [Fact]
    public void Nonprovider_usage_never_becomes_a_paid_measurement()
    {
        Assert.Null(TokenCostCalculator.Price(Call() with { UsageSource = "estimated" }, Sol).EstimatedCostUsd);
        var fixture = TokenCostCalculator.Price(Call() with { UsageSource = "fixture" }, Sol);
        Assert.Null(fixture.EstimatedCostUsd);
        Assert.Equal("partial", fixture.CostStatus);
    }
}

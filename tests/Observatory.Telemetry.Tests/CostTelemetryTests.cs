using System.Diagnostics;
using System.Diagnostics.Metrics;
using Observatory.ServiceDefaults;

namespace Observatory.Telemetry.Tests;

/// <summary>The one behaviour ServiceDefaults adds on top of the native GenAI telemetry: cache and cost facts.</summary>
public sealed class CostTelemetryTests
{
    [Fact]
    public void Model_cost_is_tagged_on_the_current_chat_span_and_counted()
    {
        using var source = new ActivitySource("Observatory.Tests.Cost");
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        var measurements = new List<(double Value, Dictionary<string, object?> Tags)>();
        using var meters = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == AiTelemetryExtensions.CostMeterName) meterListener.EnableMeasurementEvents(instrument);
            }
        };
        meters.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            measurements.Add((value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value))));
        meters.Start();

        using (var chat = source.StartActivity("chat gpt5"))
        {
            AiTelemetryExtensions.RecordModelCall("catalog", "gpt5", cachedInputTokens: 50, costUsd: 0.0125m);

            Assert.Equal(0.0125, chat!.GetTagItem("observatory.cost.usd"));
            Assert.Equal(50L, chat.GetTagItem("gen_ai.usage.cache_read.input_tokens"));
            Assert.Equal("catalog", chat.GetTagItem("observatory.agent"));
        }

        var (value, tags) = Assert.Single(measurements);
        Assert.Equal(0.0125, value, 6);
        Assert.Equal("catalog", tags["gen_ai.agent.name"]);
        Assert.Equal("gpt5", tags["observatory.model_profile"]);
    }

    [Fact]
    public void Unknown_cost_is_never_counted_as_zero()
    {
        var counted = false;
        using var meters = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == AiTelemetryExtensions.CostMeterName) meterListener.EnableMeasurementEvents(instrument);
            }
        };
        meters.SetMeasurementEventCallback<double>((_, _, _, _) => counted = true);
        meters.Start();

        AiTelemetryExtensions.RecordModelCall("router", "gpt5", cachedInputTokens: null, costUsd: null);

        Assert.False(counted);
    }
}

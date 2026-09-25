using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Observatory.ServiceDefaults;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Observatory.Telemetry.Tests;

public sealed class NativeTelemetryTests
{
    private const string PrivatePrompt = "private-prompt-not-for-otel";
    private const string PrivateAnswer = "private-answer-not-for-otel";

    [Fact]
    public async Task OfficialSdkEmitsAgentAndChatSpansAndMetersWithoutSensitiveContent()
    {
        var spans = new ConcurrentBag<Activity>();
        var measurements = new ConcurrentBag<(string Meter, string Instrument)>();
        using var activities = new ActivityListener
        {
            ShouldListenTo = source => IsAiSource(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add
        };
        ActivitySource.AddActivityListener(activities);
        using var meters = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (IsAiSource(instrument.Meter.Name))
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meters.SetMeasurementEventCallback<double>((instrument, _, _, _) =>
            measurements.Add((instrument.Meter.Name, instrument.Name)));
        meters.SetMeasurementEventCallback<long>((instrument, _, _, _) =>
            measurements.Add((instrument.Meter.Name, instrument.Name)));
        meters.Start();

        await RunOfflineAgent();

        var agent = Assert.Single(spans, span => span.Source.Name == AiTelemetryExtensions.AgentSourceName);
        var chat = Assert.Single(spans, span => span.Source.Name == AiTelemetryExtensions.ChatSourceName);
        Assert.Equal(agent.TraceId, chat.TraceId);
        Assert.Equal(agent.SpanId, chat.ParentSpanId);
        Assert.Contains(measurements, item => item.Meter == AiTelemetryExtensions.ChatSourceName && item.Instrument == "gen_ai.client.token.usage");
        Assert.Contains(measurements, item => item.Meter == AiTelemetryExtensions.ChatSourceName && item.Instrument == "gen_ai.client.operation.duration");
        Assert.Contains(measurements, item => item.Meter == AiTelemetryExtensions.AgentSourceName);
        AssertNoSensitiveContent(spans);
    }

    [Fact]
    public async Task ServiceDefaultsCollectsNativeSpansAndMetersWithoutAnOtlpEndpoint()
    {
        var spans = new SpanCollector();
        var metrics = new MetricCollector();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = ""
        });
        builder.AddServiceDefaults();
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddProcessor(spans))
            .WithMetrics(meter => meter.AddReader(new PeriodicExportingMetricReader(metrics, 60_000)));
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            await RunOfflineAgent();
            Assert.True(host.Services.GetRequiredService<MeterProvider>().ForceFlush());
            Assert.True(host.Services.GetRequiredService<TracerProvider>().ForceFlush());
            Assert.Contains(spans.Spans, span => span.Source.Name == AiTelemetryExtensions.ChatSourceName);
            Assert.Contains(spans.Spans, span => span.Source.Name == AiTelemetryExtensions.AgentSourceName);
            Assert.Contains(metrics.Instruments, item => item.Meter == AiTelemetryExtensions.ChatSourceName && item.Name == "gen_ai.client.token.usage");
            Assert.Contains(metrics.Instruments, item => item.Meter == AiTelemetryExtensions.AgentSourceName);
            AssertNoSensitiveContent(spans.Spans);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static async Task RunOfflineAgent()
    {
        using var client = new OfflineChatClient().AsBuilder().UseObservatoryTelemetry().Build();
        var agent = new ChatClientAgent(client, instructions: "Offline test only.", name: "offline-agent")
            .AsBuilder().UseObservatoryTelemetry().Build();
        var response = await agent.RunAsync(PrivatePrompt);
        Assert.Equal(PrivateAnswer, response.Text);
    }

    private static bool IsAiSource(string name) =>
        name is AiTelemetryExtensions.ChatSourceName or AiTelemetryExtensions.AgentSourceName;

    private static void AssertNoSensitiveContent(IEnumerable<Activity> spans)
    {
        foreach (var span in spans)
        {
            var text = string.Join(" ", span.TagObjects.Select(tag => $"{tag.Key}={tag.Value}")
                .Concat(span.Events.SelectMany(item => item.Tags.Select(tag => $"{tag.Key}={tag.Value}"))));
            Assert.DoesNotContain(PrivatePrompt, text);
            Assert.DoesNotContain(PrivateAnswer, text);
        }
    }

    private sealed class OfflineChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, PrivateAnswer))
            {
                ModelId = "offline-model",
                ResponseId = "offline-response",
                FinishReason = ChatFinishReason.Stop,
                Usage = new UsageDetails { InputTokenCount = 12, OutputTokenCount = 4, TotalTokenCount = 16 }
            });

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This offline fixture tests non-streaming native instrumentation.");

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType == typeof(ChatClientMetadata)
                ? new ChatClientMetadata("offline", new Uri("https://offline.invalid"), "offline-model")
                : null;

        public void Dispose() { }
    }

    private sealed class SpanCollector : BaseProcessor<Activity>
    {
        public ConcurrentBag<Activity> Spans { get; } = [];
        public override void OnEnd(Activity data) => Spans.Add(data);
    }

    private sealed class MetricCollector : BaseExporter<Metric>
    {
        public ConcurrentBag<(string Meter, string Name)> Instruments { get; } = [];
        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
                Instruments.Add((metric.MeterName, metric.Name));
            return ExportResult.Success;
        }
    }
}

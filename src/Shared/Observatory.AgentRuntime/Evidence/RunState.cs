using System.Text.Json;
using Observatory.Core;

namespace Observatory.AgentRuntime;

public sealed class RunState(AgentRunRequest request, Func<RunEvent, Task> emit)
{
    private readonly SemaphoreSlim _eventGate = new(1, 1);
    private readonly object _sync = new();
    private readonly HashSet<string> _eventIds = [];
    private readonly HashSet<int> _productIds = [];
    private readonly HashSet<string> _sources = [];
    private long _sequence;
    private int _modelCalls;
    private decimal _spent;
    private bool _unknownUsage;
    private string? _decision;
    private Exception? _failure;

    public AgentRunRequest Request { get; } = request;
    public int RemainingCalls { get { lock (_sync) return Request.Configuration.MaxModelCalls - _modelCalls; } }
    public decimal? RemainingBudget { get { lock (_sync) return Request.Configuration.ApprovedBudgetUsd - _spent; } }

    public void BeforeModelCall()
    {
        ThrowIfFaulted();
        lock (_sync)
        {
            if (!Request.Configuration.UnboundedExecution && _modelCalls >= Request.Configuration.MaxModelCalls)
                throw new DomainException("model_call_limit", "Limite complessivo di chiamate modello raggiunto.");
            if (_unknownUsage || !Request.Configuration.UnboundedExecution && _spent >= Request.Configuration.ApprovedBudgetUsd)
                throw new DomainException("budget_exhausted", "Budget esaurito o usage non verificabile: ulteriori chiamate al provider bloccate.");
            _modelCalls++;
        }
    }

    public void Fail(Exception error)
    {
        lock (_sync) _failure ??= error;
    }

    public void ThrowIfFaulted()
    {
        Exception? error;
        lock (_sync) error = _failure;
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    public Task EmitAsync(string kind, string agent, string message, object? data = null) =>
        PublishAsync(new RunEvent { RunId = Request.RunId, Kind = kind, Agent = agent, Message = message, Data = data });

    public async Task PublishAsync(RunEvent item, bool imported = false)
    {
        await _eventGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_eventIds.Add(item.Id))
                return;
            if (item.Kind == "model.completed")
            {
                var call = item.Data as ModelCallRecord
                    ?? (item.Data as JsonElement?)?.Deserialize<ModelCallRecord>(AgentJson.Options);
                if (call is null)
                    throw new InvalidOperationException("model.completed requires ModelCallRecord data.");
                lock (_sync)
                {
                    if (imported)
                        _modelCalls++;
                    _spent += call.EstimatedCostUsd ?? 0;
                    _unknownUsage |= call.EstimatedCostUsd is null;
                }
                item = item with { Data = call };
            }
            await emit(item with { RunId = Request.RunId, Sequence = ++_sequence }).ConfigureAwait(false);
        }
        finally
        {
            _eventGate.Release();
        }
    }

    public void ObserveDomain(object? value)
    {
        lock (_sync)
        {
            switch (value)
            {
                case CatalogQueryResponse query:
                    _sources.Add("catalog:DummyJSON");
                    foreach (var item in query.Products) ObserveDomain(item);
                    break;
                case CatalogFacetsResponse:
                    _sources.Add("catalog:DummyJSON");
                    break;
                case ProductFact product:
                    _productIds.Add(product.Id);
                    _sources.Add("catalog:DummyJSON");
                    break;
                case IEnumerable<ProductFact> products:
                    foreach (var item in products) ObserveDomain(item);
                    break;
                case ShopOrder order:
                    _productIds.Add(order.ProductId);
                    _sources.Add($"order:{order.Id}");
                    break;
                case ReturnAssessment assessment:
                    _sources.Add(assessment.PolicyId);
                    _sources.Add($"order:{assessment.OrderId}");
                    _decision = assessment.NeedsClarification ? "needs_clarification" : assessment.Eligible ? "allowed" : "denied";
                    break;
                case ReturnDraft draft:
                    _sources.Add($"draft:{draft.Id}");
                    _decision = "draft";
                    break;
                case IEnumerable<ShopPolicy> policies:
                    foreach (var policy in policies) _sources.Add(policy.Id);
                    break;
                case AgentExecutionResult result:
                    foreach (var id in result.ProductIds) _productIds.Add(id);
                    foreach (var source in result.Sources) _sources.Add(source);
                    if (result.Decision is not null) _decision = result.Decision;
                    break;
            }
        }
    }

    public AgentExecutionResult Result(string answer)
    {
        lock (_sync)
            return new(answer, _productIds.Order().ToArray(), _sources.Where(source => !string.IsNullOrWhiteSpace(source)).Order().ToArray(), _decision);
    }
}

public static class AgentJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false };
}

/// <summary>Per-run request forwarded to an A2A specialist in the standard message metadata extension.</summary>
public sealed record RemoteInvocation(string InvocationId, AgentRunRequest Request);

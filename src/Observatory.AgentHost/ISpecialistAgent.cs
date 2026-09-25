using Observatory.Core;

namespace Observatory.AgentHost;

public interface ISpecialistAgent
{
    Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, string query,
        Func<RunEvent, Task> emit, CancellationToken cancellationToken);
}

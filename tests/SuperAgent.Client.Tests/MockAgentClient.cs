using System.Threading;
using System.Threading.Tasks;
using SuperAgent.Core;

namespace SuperAgent.Client.Tests;

public class MockAgentClient : IAgentClient
{
    public Task<AgentResponse> GetResponseAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        var response = new AgentResponse
        {
            Content = $"Mock response to: {request.Messages.LastOrDefault()?.Content ?? "empty"}"
        };
        return Task.FromResult(response);
    }
}
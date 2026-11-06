using SuperAgent.Core;

namespace SuperAgent.Client;

public sealed class ReloadableAgentClient : IStreamingAgentClient, IDisposable
{
    private OpenAICompatClient? _currentClient;

    public ReloadableAgentClient(OpenAICompatClient initialClient)
    {
        _currentClient = initialClient ?? throw new ArgumentNullException(nameof(initialClient));
    }

    public void Replace(OpenAICompatClient replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        Interlocked.Exchange(ref _currentClient, replacement)?.Dispose();
    }

    public Task<AgentResponse> GetResponseAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        return GetCurrentClient().GetResponseAsync(request, cancellationToken);
    }

    public Task<AgentResponse> GetResponseAsync(
        AgentRequest request,
        Action<string>? onContentChunk,
        CancellationToken cancellationToken)
    {
        return GetCurrentClient().GetResponseAsync(request, onContentChunk, cancellationToken);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _currentClient, null)?.Dispose();
    }

    private OpenAICompatClient GetCurrentClient()
    {
        return Volatile.Read(ref _currentClient)
            ?? throw new ObjectDisposedException(nameof(ReloadableAgentClient));
    }
}

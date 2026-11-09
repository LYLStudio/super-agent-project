using SuperAgent.Core;

namespace SuperAgent.Client;

public sealed class ReloadableAgentClient : IStreamingAgentClient, IDisposable
{
    private IStreamingAgentClient? _currentClient;

    public ReloadableAgentClient(IStreamingAgentClient initialClient)
    {
        _currentClient = initialClient ?? throw new ArgumentNullException(nameof(initialClient));
    }

    public void Replace(IStreamingAgentClient replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        DisposeClient(Interlocked.Exchange(ref _currentClient, replacement));
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
        DisposeClient(Interlocked.Exchange(ref _currentClient, null));
    }

    private IStreamingAgentClient GetCurrentClient()
    {
        return Volatile.Read(ref _currentClient)
            ?? throw new ObjectDisposedException(nameof(ReloadableAgentClient));
    }

    private static void DisposeClient(IStreamingAgentClient? client)
    {
        (client as IDisposable)?.Dispose();
    }
}

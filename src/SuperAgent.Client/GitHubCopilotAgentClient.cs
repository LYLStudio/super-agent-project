using GitHub.Copilot;
using Microsoft.Extensions.AI;
using System.Text;
using System.Text.Json;
using SuperAgent.Core;

namespace SuperAgent.Client;

public sealed class GitHubCopilotAgentClient : IStreamingAgentClient, IDisposable
{
    private readonly CopilotClient _client;
    private readonly IToolRegistry _toolRegistry;
    private readonly string _model;
    private readonly TimeSpan _requestTimeout;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private bool _started;
    private bool _disposed;

    public GitHubCopilotAgentClient(
        IToolRegistry toolRegistry,
        string model,
        string workingDirectory,
        TimeSpan? requestTimeout = null)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _model = string.IsNullOrWhiteSpace(model)
            ? throw new ArgumentException("A Copilot model name is required.", nameof(model))
            : model;
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(120);
        if (_requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "Request timeout must be positive.");
        }

        _client = new CopilotClient(new CopilotClientOptions
        {
            WorkingDirectory = workingDirectory
        });
    }

    public Task<AgentResponse> GetResponseAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        return GetResponseAsync(request, null, cancellationToken);
    }

    public async Task<AgentResponse> GetResponseAsync(
        AgentRequest request,
        Action<string>? onContentChunk,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var timeoutCts = new CancellationTokenSource(_requestTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);
            var requestToken = linkedCts.Token;

            if (!_started)
            {
                await _client.StartAsync().ConfigureAwait(false);
                _started = true;
            }

            var dispatcher = CreateToolDispatcher(request.Tools);
            await using var session = await _client.CreateSessionAsync(new SessionConfig
            {
                Model = _model,
                Streaming = onContentChunk is not null,
                SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Append,
                    Content = string.Join(
                        "\n\n",
                        request.Messages
                            .Where(message => message.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                            .Select(message => message.Content)
                            .Where(content => !string.IsNullOrWhiteSpace(content)))
                },
                Tools = [dispatcher],
                AvailableTools = ["superagent_tool"],
                OnPermissionRequest = PermissionHandler.ApproveAll
            }).ConfigureAwait(false);

            if (onContentChunk is not null)
            {
                session.On<SessionEvent>(sessionEvent =>
                {
                    if (sessionEvent is AssistantMessageDeltaEvent deltaEvent &&
                        !string.IsNullOrEmpty(deltaEvent.Data.DeltaContent))
                    {
                        onContentChunk(deltaEvent.Data.DeltaContent);
                    }
                });
            }

            var response = await session.SendAndWaitAsync(
                CreatePrompt(request),
                _requestTimeout,
                requestToken).ConfigureAwait(false);

            return new AgentResponse
            {
                Content = response?.Data.Content ?? string.Empty
            };
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private AIFunction CreateToolDispatcher(IReadOnlyList<ToolDefinition> tools)
    {
        var toolIndex = JsonSerializer.Serialize(tools.Select(tool => new
        {
            tool.Name,
            tool.Description,
            Parameters = tool.Parameters.ValueKind == JsonValueKind.Undefined
                ? (JsonElement?)null
                : tool.Parameters
        }));

        return CopilotTool.DefineTool(
            (string name, JsonElement arguments, CancellationToken cancellationToken) =>
                _toolRegistry.ExecuteAsync(name, arguments, cancellationToken),
            factoryOptions: new AIFunctionFactoryOptions
            {
                Name = "superagent_tool",
                Description = $"Execute one SuperAgent tool. Select the tool by name and pass its arguments as JSON. Available tools and schemas: {toolIndex}"
            });
    }

    private static string CreatePrompt(AgentRequest request)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("You are SuperAgent. Follow the system instructions and answer in the requested language.");
        prompt.AppendLine("Use the superagent_tool function to execute available tools; do not claim a tool ran unless it returned a result.");
        prompt.AppendLine();
        prompt.AppendLine("Conversation:");

        foreach (var message in request.Messages)
        {
            if (message.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            prompt.Append(message.Role);
            prompt.Append(": ");
            prompt.AppendLine(message.Content);
            if (message.ToolCalls.Count > 0)
            {
                prompt.Append("Tool calls: ");
                prompt.AppendLine(JsonSerializer.Serialize(message.ToolCalls));
            }
        }

        return prompt.ToString();
    }

    public void Dispose()
    {
        _requestGate.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_started)
            {
                _client.StopAsync().GetAwaiter().GetResult();
                _started = false;
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }
}

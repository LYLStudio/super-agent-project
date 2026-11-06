using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SuperAgent.Core;

/// <summary>
/// Agent Loop 執行狀態
/// </summary>
public enum AgentLoopState
{
    Thinking,
    Planning,
    Acting,
    Observing,
    Completed,
    Failed
}

/// <summary>
/// Agent Loop 消息
/// </summary>
public class AgentMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ToolCallId { get; set; }
    public string? Name { get; set; }
    public List<ToolCall> ToolCalls { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 工具呼叫定義
/// </summary>
public class ToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public JsonElement Arguments { get; set; }
}

/// <summary>
/// Agent Loop 執行結果
/// </summary>
public class AgentLoopResult
{
    public bool Success { get; set; }
    public string FinalContent { get; set; } = string.Empty;
    public List<AgentMessage> Messages { get; set; } = new();
    public AgentLoopState FinalState { get; set; }
    public string? Error { get; set; }
    public int TotalIterations { get; set; }
}

/// <summary>
/// Agent Loop 設定
/// </summary>
public class AgentLoopConfig
{
    public int MaxIterations { get; set; } = 10;
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 4000;
    public bool EnableToolChoice { get; set; } = true;
    public bool ShowThinkingProcess { get; set; }
}

/// <summary>
/// Agent Loop 主體 - 強悍的 Agent Loop 能力
/// 有序思考、規劃、執行與觀察工具
/// </summary>
public class AgentLoop
{
    private readonly IAgentClient _client;
    private readonly IToolRegistry _toolRegistry;
    private readonly AgentLoopConfig _config;
    private readonly RetryPolicy _retryPolicy;
    private readonly ConversationStorage? _conversationStorage;
    private readonly string? _conversationId;
    private readonly List<AgentMessage> _messages = new();
    private readonly PerformanceMonitor _performanceMonitor = new();

    public event Action<int, AgentMessage>? ThinkingResponseReceived;
    public event Action<int, string>? ResponseChunkReceived;
    public event Action<ToolCall>? ToolExecutionStarted;
    public event Action<ToolCall, string>? ToolExecutionCompleted;

    /// <summary>
    /// 建立 Agent Loop
    /// </summary>
    public AgentLoop(IAgentClient client, IToolRegistry toolRegistry, AgentLoopConfig? config = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _config = config ?? new AgentLoopConfig();
        _retryPolicy = new RetryPolicy(maxRetries: 3, initialDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// 建立支援會話儲存的 Agent Loop
    /// </summary>
    public AgentLoop(IAgentClient client, IToolRegistry toolRegistry, ConversationStorage conversationStorage, string conversationId, AgentLoopConfig? config = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _config = config ?? new AgentLoopConfig();
        _retryPolicy = new RetryPolicy(maxRetries: 3, initialDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(30));
        _conversationStorage = conversationStorage;
        _conversationId = conversationId;
    }

    /// <summary>
    /// 執行 Agent Loop
    /// </summary>
    public async Task<AgentLoopResult> RunAsync(string userInput, CancellationToken cancellationToken = default)
    {
        // 載入歷史會話 (如果支援會話儲存)
        if (_conversationStorage is not null && _conversationId is not null)
        {
            var historicalMessages = await _conversationStorage.LoadConversationAsync(_conversationId, cancellationToken);
            if (historicalMessages != null && historicalMessages.Count > 0)
            {
                _messages.AddRange(historicalMessages);
            }
            else
            {
                // 初始化系統訊息
                _messages.Add(new AgentMessage
                {
                    Role = "system",
                    Content = GetSystemPrompt()
                });
            }
        }
        else
        {
            // 初始化系統訊息
            _messages.Add(new AgentMessage
            {
                Role = "system",
                Content = GetSystemPrompt()
            });
        }

        // 加入使用者輸入
        _messages.Add(new AgentMessage
        {
            Role = "user",
            Content = userInput
        });

        AgentLoopState state = AgentLoopState.Thinking;
        int iteration = 0;

        while (iteration < _config.MaxIterations && state != AgentLoopState.Completed && state != AgentLoopState.Failed)
        {
            iteration++;
            state = AgentLoopState.Thinking;

            // 步驟 1: 思考 - 分析當前狀態與任務 (支援重試)
            AgentMessage thoughtResponse;
            try
            {
                var response = await _retryPolicy.ExecuteAsync(
                    async (ct) => await GenerateResponseAsync(ct, iteration),
                    "GenerateResponse",
                    cancellationToken);
                thoughtResponse = new AgentMessage
                {
                    Role = "assistant",
                    Content = response.Content ?? string.Empty,
                    ToolCalls = response.ToolCalls.Select(tc => new ToolCall
                    {
                        Id = tc.Id,
                        Name = tc.Name,
                        Arguments = tc.Arguments
                    }).ToList()
                };
            }
            catch
            {
                // 重試失敗後，建立錯誤回應訊息
                thoughtResponse = new AgentMessage
                {
                    Role = "assistant",
                    Content = $"錯誤: API 請求失敗後重試 {_config.MaxIterations} 次後仍然失敗。請檢查 API 端點與密鑰設定。",
                    ToolCalls = new List<ToolCall>()
                };
            }

            if (_config.ShowThinkingProcess)
            {
                ThinkingResponseReceived?.Invoke(iteration, thoughtResponse);
            }

            // 檢查是否為錯誤回應
            if (thoughtResponse.Content.StartsWith("錯誤:"))
            {
                state = AgentLoopState.Failed;
                _messages.Add(thoughtResponse);
                break;
            }

            if (thoughtResponse.ToolCalls.Count > 0 && _config.EnableToolChoice)
            {
                // 步驟 2: 規劃 - 決定哪些工具需要執行
                state = AgentLoopState.Planning;
                await ExecuteToolCallsAsync(thoughtResponse.ToolCalls, cancellationToken);
                state = AgentLoopState.Observing;
                
                // 加入工具結果到對話歷史
                foreach (var toolCall in thoughtResponse.ToolCalls)
                {
                    var toolResult = await _toolRegistry.GetLastResultAsync(toolCall.Id);
                    if (toolResult != null)
                    {
                        _messages.Add(new AgentMessage
                        {
                            Role = "tool",
                            Content = toolResult,
                            ToolCallId = toolCall.Id,
                            Name = toolCall.Name
                        });
                    }
                }
            }
            else
            {
                // 無需工具，直接回應
                state = AgentLoopState.Completed;
                _messages.Add(thoughtResponse);
                break;
            }
        }

        if (state != AgentLoopState.Completed)
        {
            state = AgentLoopState.Failed;
        }

        // 儲存會話 (如果支援會話儲存)
        if (_conversationStorage is not null && _conversationId is not null)
        {
            try
            {
                await _conversationStorage.SaveConversationAsync(_conversationId, _messages, cancellationToken);
            }
            catch
            {
                // 會話儲存失敗不應中斷主流程
            }
        }

        return new AgentLoopResult
        {
            Success = state == AgentLoopState.Completed,
            FinalContent = _messages.LastOrDefault()?.Content ?? string.Empty,
            Messages = _messages.ToList(),
            FinalState = state,
            Error = state == AgentLoopState.Failed ? "超過最大迭代次數" : null,
            TotalIterations = iteration
        };
    }

    private async Task<AgentMessage> GenerateResponseAsync(CancellationToken cts, int iteration)
    {
        var request = new AgentRequest
        {
            Messages = _messages.Select(m => new ChatMessage
            {
                Role = m.Role,
                Content = m.Content,
                ToolCalls = m.ToolCalls?.Select(tc => new ToolCallDefinition
                {
                    Id = tc.Id,
                    Name = tc.Name,
                    Arguments = tc.Arguments
                }).ToList() ?? new()
            }).ToList(),
            Tools = _toolRegistry.GetAllTools()
                .Select(t => new ToolDefinition
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.Parameters
                }).ToList(),
            Temperature = _config.Temperature,
            MaxTokens = _config.MaxTokens
        };

        var response = _config.ShowThinkingProcess && _client is IStreamingAgentClient streamingClient
            ? await streamingClient.GetResponseAsync(
                request,
                chunk => ResponseChunkReceived?.Invoke(iteration, chunk),
                cts)
            : await _client.GetResponseAsync(request, cts);

        return new AgentMessage
        {
            Role = "assistant",
            Content = response.Content ?? string.Empty,
            ToolCalls = response.ToolCalls?.Select(tc => new ToolCall
            {
                Id = tc.Id,
                Name = tc.Name,
                Arguments = tc.Arguments
            }).ToList() ?? new()
        };
    }

    private async Task ExecuteToolCallsAsync(List<ToolCall> toolCalls, CancellationToken cts)
    {
        foreach (var toolCall in toolCalls)
        {
            ToolExecutionStarted?.Invoke(toolCall);
            try
            {
                var result = await _toolRegistry.ExecuteAsync(toolCall.Name, toolCall.Arguments, cts);
                _toolRegistry.SetResult(toolCall.Id, result);
                ToolExecutionCompleted?.Invoke(toolCall, result);
            }
            catch (Exception ex)
            {
                var result = $"Error: {ex.Message}";
                _toolRegistry.SetResult(toolCall.Id, result);
                ToolExecutionCompleted?.Invoke(toolCall, result);
            }
        }
    }

    private string GetSystemPrompt()
    {
        return @"你是一個強悍的 AI Agent，名為 SuperAgent。你有以下能力：

1. **思考與規劃**: 你可以分析複雜任務，將其拆解為小步驟。
2. **工具使用**: 你可以選擇並執行適當的工具來完成任務。
3. **程式開發**: 你可以撰寫、修改與維護程式碼。
4. **自主學習**: 你可以從工具結果中學習並調整策略。

你的目標是協助使用者完成各種任務，包括但不限於：
- 撰寫程式碼
- 除錯與修复程式
- 系統管理與自動化
- 資料分析與處理

請始終：
1. 先思考任務需要什麼工具
2. 規劃執行順序
3. 執行工具並觀察結果
4. 根據結果調整策略

當任務完成時，提供清晰的總結。";
    }
}

/// <summary>
/// Agent Loop 請求
/// </summary>
public class AgentRequest
{
    public List<ChatMessage> Messages { get; set; } = new();
    public List<ToolDefinition> Tools { get; set; } = new();
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 4000;
}

/// <summary>
/// 對話訊息
/// </summary>
public class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<ToolCallDefinition> ToolCalls { get; set; } = new();
}

/// <summary>
/// 工具定義
/// </summary>
public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonElement Parameters { get; set; }
}

/// <summary>
/// 工具呼叫定義 (請求用)
/// </summary>
public class ToolCallDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public JsonElement Arguments { get; set; }
}

/// <summary>
/// Agent Loop 回應
/// </summary>
public class AgentResponse
{
    public string? Content { get; set; }
    public List<ToolCallDefinition> ToolCalls { get; set; } = new();
}

/// <summary>
/// IAgentClient 介面
/// </summary>
public interface IAgentClient
{
    Task<AgentResponse> GetResponseAsync(AgentRequest request, CancellationToken cancellationToken);
}

public interface IStreamingAgentClient : IAgentClient
{
    Task<AgentResponse> GetResponseAsync(
        AgentRequest request,
        Action<string>? onContentChunk,
        CancellationToken cancellationToken);
}

/// <summary>
/// IToolRegistry 介面
/// </summary>
public interface IToolRegistry
{
    List<ToolDefinition> GetAllTools();
    Task<string> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken);
    Task<string?> GetLastResultAsync(string toolCallId);
    void SetResult(string toolCallId, string result);
}

/// <summary>
/// ITool 介面 - 個別工具
/// </summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonElement Parameters { get; }
    Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken);
}
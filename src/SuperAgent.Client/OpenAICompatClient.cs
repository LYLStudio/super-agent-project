using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SuperAgent.Core;

namespace SuperAgent.Client;

/// <summary>
/// OpenAI 相容 API 客戶端
/// 支援 OpenAI 規格相容的 API 端點
/// 支援 API 密鑰輪替與模型動態選擇
/// </summary>
public class OpenAICompatClient : IAgentClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly Func<string> _apiKeyProvider;
    private readonly Func<string> _modelProvider;

    /// <summary>
    /// 建立 OpenAICompatClient
    /// </summary>
    /// <param name="baseUrl">API 基礎 URL</param>
    /// <param name="apiKeyProvider">API 密鑰提供者委託</param>
    /// <param name="modelProvider">模型名稱提供者委託</param>
    public OpenAICompatClient(string baseUrl, Func<string> apiKeyProvider, Func<string> modelProvider)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
        _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKeyProvider()}");
    }

    /// <summary>
    /// 取得 AI 回應
    /// </summary>
    public async Task<AgentResponse> GetResponseAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        // 每次請求前獲取最新的 API 密鑰與模型 (支援輪替)
        var apiKey = _apiKeyProvider();
        var model = _modelProvider();

        var chatMessages = request.Messages.Select(m => new
        {
            role = m.Role,
            content = m.Content,
            tool_calls = m.ToolCalls.Count > 0 ? m.ToolCalls.Select(tc => new
            {
                id = tc.Id,
                type = "function",
                function = new
                {
                    name = tc.Name,
                    arguments = tc.Arguments.GetRawText()
                }
            }).ToList() : null
        }).ToList();

        var tools = request.Tools.Select(t => new
        {
            type = "function",
            function = new
            {
                name = t.Name,
                description = t.Description,
                parameters = JsonSerializer.Serialize(t.Parameters, new JsonSerializerOptions
                {
                    WriteIndented = false
                })
            }
        }).ToList();

        var requestBody = new
        {
            model = model,
            messages = chatMessages,
            tools = tools.Count > 0 ? tools : null,
            temperature = request.Temperature,
            max_tokens = request.MaxTokens,
            stream = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}/chat/completions", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseDoc = JsonDocument.Parse(responseJson);

        var choice = responseDoc.RootElement.GetProperty("choices")[0];
        var message = choice.GetProperty("message");

        var agentResponse = new AgentResponse
        {
            Content = message.GetProperty("content").GetString() ?? string.Empty
        };

        // 處理 tool_calls
        if (message.TryGetProperty("tool_calls", out var toolCallsElement))
        {
            foreach (var tc in toolCallsElement.EnumerateArray())
            {
                agentResponse.ToolCalls.Add(new ToolCallDefinition
                {
                    Id = tc.GetProperty("id").GetString() ?? string.Empty,
                    Name = tc.GetProperty("function").GetProperty("name").GetString() ?? string.Empty,
                    Arguments = tc.GetProperty("function").GetProperty("arguments").GetRawText() == ""
                        ? JsonElement.Parse("{}")
                        : JsonElement.Parse(tc.GetProperty("function").GetProperty("arguments").GetString() ?? "{}")
                });
            }
        }

        return agentResponse;
    }
}

/// <summary>
/// OpenAICompatClient 工廠 - 從設定建立客戶端
/// </summary>
public static class OpenAICompatClientFactory
{
    /// <summary>
    /// 建立客戶端 (傳統模式，固定密鑰與模型)
    /// </summary>
    public static IAgentClient Create(
        string baseUrl,
        string apiKey,
        string model = "qwen3-coder:30b")
    {
        return new OpenAICompatClient(baseUrl,
            () => apiKey,
            () => model);
    }

    /// <summary>
    /// 建立支援密鑰輪替的客戶端
    /// </summary>
    /// <param name="baseUrl">API 基礎 URL</param>
    /// <param name="apiKeyManager">API 密鑰管理器</param>
    /// <param name="modelSelector">模型選擇器</param>
    /// <param name="defaultModel">預設模型</param>
    public static IAgentClient CreateWithKeyRotation(
        string baseUrl,
        ApiKeyManager apiKeyManager,
        ModelSelector? modelSelector = null,
        string? defaultModel = null)
    {
        var defaultModelName = defaultModel ?? "qwen3-coder:30b";
        var selector = modelSelector ?? new ModelSelector();

        // 返回一個會在每次請求前輪換密鑰並選擇模型的客戶端
        return new OpenAICompatClient(baseUrl,
            apiKeyProvider: () =>
            {
                var key = apiKeyManager.GetCurrentKey();
                if (key is null)
                {
                    throw new InvalidOperationException("沒有可用的 API 密鑰");
                }
                return key;
            },
            modelProvider: () =>
            {
                // 每次請求前可根據上下文選擇模型，此處先使用預設模型
                // 模型選擇邏輯可在 AgentLoop 層級實作
                return selector.GetDefaultModel() ?? defaultModelName;
            });
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SuperAgent.Core;

namespace SuperAgent.Client;

/// <summary>
/// OpenAI 相容 API 客戶端
/// 支援 OpenAI 規格相容的 API 端點
/// 支援 API 密鑰輪替與模型動態選擇
/// </summary>
public class OpenAICompatClient : IStreamingAgentClient, IDisposable
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly Func<string> _apiKeyProvider;
    private readonly Func<string> _modelProvider;
    private readonly TimeSpan _requestTimeout;

    /// <summary>
    /// 建立 OpenAICompatClient
    /// </summary>
    /// <param name="baseUrl">API 基礎 URL</param>
    /// <param name="apiKeyProvider">API 密鑰提供者委託</param>
    /// <param name="modelProvider">模型名稱提供者委託</param>
    public OpenAICompatClient(
        string baseUrl,
        Func<string> apiKeyProvider,
        Func<string> modelProvider,
        HttpMessageHandler? httpMessageHandler = null,
        TimeSpan? requestTimeout = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
        _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        var timeout = requestTimeout ?? TimeSpan.FromSeconds(100);
        if (timeout != Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "Request timeout must be positive or infinite.");
        }
        _requestTimeout = timeout;
        _httpClient = new HttpClient(httpMessageHandler ?? new HttpClientHandler());
        _httpClient.Timeout = timeout;
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

        var json = JsonSerializer.Serialize(CreateRequestBody(request, model, stream: false), RequestJsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = content
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var responseDoc = JsonDocument.Parse(responseJson);

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

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/models");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKeyProvider());

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var models) ||
            models.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("模型清單回應缺少有效的 'data' 陣列。");
        }

        var names = new List<string>();
        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models.EnumerateArray())
        {
            var name = model.TryGetProperty("id", out var id)
                ? id.GetString()
                : model.TryGetProperty("name", out var modelName)
                    ? modelName.GetString()
                    : null;
            if (!string.IsNullOrWhiteSpace(name) && uniqueNames.Add(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    public async Task<AgentResponse> GetResponseAsync(
        AgentRequest request,
        Action<string>? onContentChunk,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = _requestTimeout == Timeout.InfiniteTimeSpan
            ? null
            : new CancellationTokenSource(_requestTimeout);
        using var linkedCts = timeoutCts is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var requestToken = linkedCts?.Token ?? cancellationToken;

        var apiKey = _apiKeyProvider();
        var model = _modelProvider();
        var json = JsonSerializer.Serialize(CreateRequestBody(request, model, stream: true), RequestJsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = content
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            requestToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(requestToken);
        using var reader = new StreamReader(responseStream);
        var responseContent = new StringBuilder();
        var toolCalls = new SortedDictionary<int, StreamedToolCall>();

        while (await reader.ReadLineAsync(requestToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line.AsSpan("data:".Length).Trim();
            if (data.SequenceEqual("[DONE]"))
            {
                break;
            }

            using var chunkDocument = JsonDocument.Parse(data.ToString());
            var choices = chunkDocument.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
            {
                continue;
            }

            var delta = choices[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var contentDelta) &&
                contentDelta.ValueKind == JsonValueKind.String)
            {
                var text = contentDelta.GetString() ?? string.Empty;
                if (text.Length > 0)
                {
                    responseContent.Append(text);
                    onContentChunk?.Invoke(text);
                }
            }

            if (!delta.TryGetProperty("tool_calls", out var toolCallDeltas))
            {
                continue;
            }

            foreach (var toolCallDelta in toolCallDeltas.EnumerateArray())
            {
                var index = toolCallDelta.GetProperty("index").GetInt32();
                if (!toolCalls.TryGetValue(index, out var streamedToolCall))
                {
                    streamedToolCall = new StreamedToolCall();
                    toolCalls.Add(index, streamedToolCall);
                }

                if (toolCallDelta.TryGetProperty("id", out var id))
                {
                    streamedToolCall.Id.Append(id.GetString());
                }

                if (toolCallDelta.TryGetProperty("function", out var function))
                {
                    if (function.TryGetProperty("name", out var name))
                    {
                        streamedToolCall.Name.Append(name.GetString());
                    }

                    if (function.TryGetProperty("arguments", out var arguments))
                    {
                        streamedToolCall.Arguments.Append(arguments.GetString());
                    }
                }
            }
        }

        var agentResponse = new AgentResponse { Content = responseContent.ToString() };
        foreach (var streamedToolCall in toolCalls.Values)
        {
            var arguments = string.IsNullOrWhiteSpace(streamedToolCall.Arguments.ToString())
                ? "{}"
                : streamedToolCall.Arguments.ToString();

            agentResponse.ToolCalls.Add(new ToolCallDefinition
            {
                Id = streamedToolCall.Id.ToString(),
                Name = streamedToolCall.Name.ToString(),
                Arguments = JsonDocument.Parse(arguments).RootElement.Clone()
            });
        }

        return agentResponse;
    }

    private static object CreateRequestBody(AgentRequest request, string model, bool stream)
    {
        var chatMessages = request.Messages.Select(m => new
        {
            role = m.Role,
            content = m.Content,
            tool_call_id = m.ToolCallId,
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
                parameters = t.Parameters
            }
        }).ToList();

        return new
        {
            model,
            messages = chatMessages,
            tools = tools.Count > 0 ? tools : null,
            temperature = request.Temperature,
            max_tokens = request.MaxTokens,
            stream
        };
    }

    private sealed class StreamedToolCall
    {
        public StringBuilder Id { get; } = new();
        public StringBuilder Name { get; } = new();
        public StringBuilder Arguments { get; } = new();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
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
    public static OpenAICompatClient Create(
        string baseUrl,
        string apiKey,
        string model = "gpt-oss:20b",
        TimeSpan? requestTimeout = null)
    {
        return new OpenAICompatClient(baseUrl,
            () => apiKey,
            () => model,
            requestTimeout: requestTimeout);
    }

    /// <summary>
    /// 建立支援密鑰輪替的客戶端
    /// </summary>
    /// <param name="baseUrl">API 基礎 URL</param>
    /// <param name="apiKeyManager">API 密鑰管理器</param>
    /// <param name="modelSelector">模型選擇器</param>
    /// <param name="defaultModel">預設模型</param>
    public static OpenAICompatClient CreateWithKeyRotation(
        string baseUrl,
        ApiKeyManager apiKeyManager,
        ModelSelector? modelSelector = null,
        string? defaultModel = "gpt-oss:20b",
        TimeSpan? requestTimeout = null)
    {
        var defaultModelName = defaultModel ?? "gpt-oss:20b";
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
                return string.IsNullOrWhiteSpace(defaultModel)
                    ? selector.GetDefaultModel()
                    : defaultModelName;
            },
            requestTimeout: requestTimeout);
    }
}

using System.Text;
using System.Text.Json;
using SuperAgent.Core;

namespace SuperAgent.Client;

/// <summary>
/// OpenAI 相容 API 客戶端
/// 支援 OpenAI 規格相容的 API 端點
/// </summary>
public class OpenAICompatClient : IAgentClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _model;

    public OpenAICompatClient(string baseUrl, string apiKey, string model)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _model = model;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    /// <summary>
    /// 取得 AI 回應
    /// </summary>
    public async Task<AgentResponse> GetResponseAsync(AgentRequest request, CancellationToken cancellationToken)
    {
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
            model = _model,
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
    public static IAgentClient Create(
        string baseUrl,
        string apiKey,
        string model = "qwen3-coder:30b")
    {
        return new OpenAICompatClient(baseUrl, apiKey, model);
    }
}
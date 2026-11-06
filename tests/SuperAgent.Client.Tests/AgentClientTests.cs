using Xunit;
using SuperAgent.Core;
using SuperAgent.Client;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SuperAgent.Client.Tests;

public class AgentClientTests
{
    [Fact]
    public void OpenAICompatClient_CreatesSuccessfully()
    {
        var client = OpenAICompatClientFactory.Create("http://localhost:8080", "test-key", "qwen3-coder:30b");
        Assert.NotNull(client);
    }

    [Fact]
    public void OpenAICompatClientFactory_CreatesClient()
    {
        var client = OpenAICompatClientFactory.Create("http://localhost:8080", "test-key");
        Assert.NotNull(client);
    }

    [Fact]
    public void OpenAICompatClientFactory_CreateWithKeyRotation_Success()
    {
        var apiKeyManager = new ApiKeyManager("test-encryption-key-0000000000000000");
        apiKeyManager.AddKey("my-key", "sk-test1234567890");

        var client = OpenAICompatClientFactory.CreateWithKeyRotation(
            "http://localhost:8080",
            apiKeyManager);

        Assert.NotNull(client);
    }

    [Fact]
    public async Task OpenAICompatClient_StreamsContentAndToolCalls()
    {
        const string streamedResponse =
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"規劃中\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"shell_exec\",\"arguments\":\"{\\\"command\\\":\"}}]},\"finish_reason\":null}]}\n\n" +
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"dotnet --version\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}\n\n" +
            "data: [DONE]\n\n";
        var handler = new StreamingResponseHandler(streamedResponse);
        var client = new OpenAICompatClient(
            "http://localhost:11434/v1",
            () => "test-key",
            () => "test-model",
            handler);
        var chunks = new List<string>();
        var request = new AgentRequest
        {
            Messages = { new ChatMessage { Role = "user", Content = "test" } },
            Tools =
            {
                new ToolDefinition
                {
                    Name = "shell_exec",
                    Parameters = JsonSerializer.SerializeToElement(new { type = "object" })
                }
            }
        };

        var response = await client.GetResponseAsync(request, chunks.Add, CancellationToken.None);

        Assert.Equal("規劃中", string.Concat(chunks));
        Assert.Equal("規劃中", response.Content);
        var toolCall = Assert.Single(response.ToolCalls);
        Assert.Equal("call_1", toolCall.Id);
        Assert.Equal("shell_exec", toolCall.Name);
        Assert.Equal("dotnet --version", toolCall.Arguments.GetProperty("command").GetString());
        Assert.Equal(HttpMethod.Post, handler.RequestMethod);
        Assert.Equal("Bearer test-key", handler.AuthorizationHeader);
        Assert.True(handler.RequestBody!.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(JsonValueKind.Object,
            handler.RequestBody.RootElement.GetProperty("tools")[0]
                .GetProperty("function").GetProperty("parameters").ValueKind);
    }

    [Fact]
    public async Task OpenAICompatClient_GetsAvailableModelsFromOpenAiCompatibleApi()
    {
        const string modelResponse =
            "{\"object\":\"list\",\"data\":[{\"id\":\"model-a\"},{\"id\":\"model-b\"},{\"name\":\"model-c\"},{\"id\":\"model-a\"}]}";
        var handler = new ModelListResponseHandler(modelResponse);
        var client = new OpenAICompatClient(
            "http://localhost:11434/v1",
            () => "test-key",
            () => "configured-model",
            handler);

        var models = await client.GetAvailableModelsAsync();

        Assert.Equal(new[] { "model-a", "model-b", "model-c" }, models);
        Assert.Equal(HttpMethod.Get, handler.RequestMethod);
        Assert.Equal("/v1/models", handler.RequestPath);
        Assert.Equal("Bearer test-key", handler.AuthorizationHeader);
    }

    [Fact]
    public async Task OpenAICompatClient_RejectsMalformedModelListResponse()
    {
        var client = new OpenAICompatClient(
            "http://localhost:11434/v1",
            () => "test-key",
            () => "configured-model",
            new ModelListResponseHandler("{\"models\":[]}"));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAvailableModelsAsync());
    }

    private sealed class StreamingResponseHandler(string responseBody) : HttpMessageHandler
    {
        public HttpMethod? RequestMethod { get; private set; }
        public string? AuthorizationHeader { get; private set; }
        public JsonDocument? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestMethod = request.Method;
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            RequestBody = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "text/event-stream")
            };
        }
    }

    private sealed class ModelListResponseHandler(string responseBody) : HttpMessageHandler
    {
        public HttpMethod? RequestMethod { get; private set; }
        public string? RequestPath { get; private set; }
        public string? AuthorizationHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestMethod = request.Method;
            RequestPath = request.RequestUri?.AbsolutePath;
            AuthorizationHeader = request.Headers.Authorization?.ToString();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}

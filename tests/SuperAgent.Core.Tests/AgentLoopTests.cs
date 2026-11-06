using Xunit;
using SuperAgent.Core;
using System.Text.Json;

namespace SuperAgent.Core.Tests;

public class AgentLoopTests
{
    [Fact]
    public void AgentLoopState_HasAllValues()
    {
        Assert.Equal(AgentLoopState.Thinking, AgentLoopState.Thinking);
        Assert.Equal(AgentLoopState.Planning, AgentLoopState.Planning);
        Assert.Equal(AgentLoopState.Acting, AgentLoopState.Acting);
        Assert.Equal(AgentLoopState.Observing, AgentLoopState.Observing);
        Assert.Equal(AgentLoopState.Completed, AgentLoopState.Completed);
        Assert.Equal(AgentLoopState.Failed, AgentLoopState.Failed);
    }

    [Fact]
    public void AgentMessage_CreatesWithDefaults()
    {
        var msg = new AgentMessage { Role = "user", Content = "Hello" };
        Assert.Equal("user", msg.Role);
        Assert.Equal("Hello", msg.Content);
    }

    [Fact]
    public void ToolCall_CreatesWithDefaults()
    {
        var call = new ToolCall { Id = "1", Name = "test" };
        Assert.Equal("1", call.Id);
        Assert.Equal("test", call.Name);
    }

    [Fact]
    public void AgentLoopConfig_HasDefaults()
    {
        var config = new AgentLoopConfig();
        Assert.Equal(10, config.MaxIterations);
        Assert.Equal(0.7, config.Temperature, 0.001);
        Assert.Equal(4000, config.MaxTokens);
        Assert.Equal(32000, config.MaxContextCharacters);
        Assert.True(config.EnableToolChoice);
        Assert.False(config.ShowThinkingProcess);
    }

    [Fact]
    public async Task AgentLoop_ReportsThinkingResponsesOnlyWhenEnabled()
    {
        var client = new IntegrationMockClient();
        var registry = new ToolRegistry();
        var hiddenAgent = new AgentLoop(client, registry, new AgentLoopConfig
        {
            ShowThinkingProcess = false
        });
        var visibleAgent = new AgentLoop(client, registry, new AgentLoopConfig
        {
            ShowThinkingProcess = true
        });
        var thinkingResponses = new List<(int Iteration, AgentMessage Response)>();
        visibleAgent.ThinkingResponseReceived += (iteration, response) =>
            thinkingResponses.Add((iteration, response));

        await hiddenAgent.RunAsync("測試隱藏思考過程");
        await visibleAgent.RunAsync("測試顯示思考過程");

        var thinkingResponse = Assert.Single(thinkingResponses);
        Assert.Equal(1, thinkingResponse.Iteration);
        Assert.Contains("測試顯示思考過程", thinkingResponse.Response.Content);
    }

    [Fact]
    public async Task AgentLoop_SendsAssistantToolCallsBeforeToolResults()
    {
        var client = new RecordingAgentClient(returnToolCallFirst: true);
        var registry = new ToolRegistry();
        registry.Register(new FixedResultTool());
        var agent = new AgentLoop(client, registry);

        var result = await agent.RunAsync("run the tool");

        Assert.True(result.Success);
        var followUpRequest = client.Requests[1];
        var assistantMessage = Assert.Single(followUpRequest.Messages.Where(message => message.Role == "assistant"));
        var toolCall = Assert.Single(assistantMessage.ToolCalls);
        Assert.Equal("call-1", toolCall.Id);
        var toolMessage = Assert.Single(followUpRequest.Messages.Where(message => message.Role == "tool"));
        Assert.Equal(toolCall.Id, toolMessage.ToolCallId);
        Assert.Equal("tool result", toolMessage.Content);
    }

    [Fact]
    public async Task AgentLoop_ReusesSystemPromptAcrossRuns()
    {
        var client = new RecordingAgentClient();
        var agent = new AgentLoop(client, new ToolRegistry());

        await agent.RunAsync("first task");
        await agent.RunAsync("second task");

        var secondRequest = client.Requests[1];
        Assert.Single(secondRequest.Messages.Where(message => message.Role == "system"));
        Assert.Equal(2, secondRequest.Messages.Count(message => message.Role == "user"));
    }

    [Fact]
    public async Task AgentLoop_IncludesDefaultPromptRegionAndLanguageInSystemMessage()
    {
        var client = new RecordingAgentClient();
        var agent = new AgentLoop(client, new ToolRegistry(), new AgentLoopConfig
        {
            DefaultSystemPrompt = "Prefer concise explanations.",
            Region = "日本",
            Language = "日本語"
        });

        await agent.RunAsync("hello");

        var systemPrompt = Assert.Single(client.Requests[0].Messages
            .Where(message => message.Role == "system")).Content;
        Assert.Contains("Prefer concise explanations.", systemPrompt);
        Assert.Contains("地區脈絡：日本", systemPrompt);
        Assert.Contains("回覆語系：日本語", systemPrompt);
    }

    [Fact]
    public async Task AgentLoop_UpdatesSystemPromptWhenConfigurationChangesBetweenRuns()
    {
        var client = new RecordingAgentClient();
        var config = new AgentLoopConfig
        {
            DefaultSystemPrompt = "First instructions",
            Region = "台灣",
            Language = "繁體中文"
        };
        var agent = new AgentLoop(client, new ToolRegistry(), config);

        await agent.RunAsync("first task");
        config.DefaultSystemPrompt = "Updated instructions";
        config.Region = "加拿大";
        config.Language = "English";
        await agent.RunAsync("second task");

        var secondRequest = client.Requests[1];
        var systemPrompt = Assert.Single(secondRequest.Messages
            .Where(message => message.Role == "system")).Content;
        Assert.Contains("Updated instructions", systemPrompt);
        Assert.Contains("地區脈絡：加拿大", systemPrompt);
        Assert.Contains("回覆語系：English", systemPrompt);
        Assert.DoesNotContain("First instructions", systemPrompt);
    }

    [Fact]
    public async Task AgentLoop_TrimsOlderTurnsToContextBudget()
    {
        var client = new RecordingAgentClient();
        var agent = new AgentLoop(client, new ToolRegistry(), new AgentLoopConfig
        {
            MaxContextCharacters = 1200
        });

        await agent.RunAsync(new string('a', 600));
        await agent.RunAsync(new string('b', 600));

        var secondRequest = client.Requests[1];
        Assert.Single(secondRequest.Messages.Where(message => message.Role == "user"));
        Assert.Contains("較早的對話回合因上下文預算限制而省略",
            Assert.Single(secondRequest.Messages.Where(message => message.Role == "system")).Content);
    }

    [Fact]
    public async Task AgentLoop_TruncatesToolOutputWithoutBreakingToolCallPair()
    {
        var client = new RecordingAgentClient(returnToolCallFirst: true);
        var registry = new ToolRegistry();
        registry.Register(new FixedResultTool(new string('x', 5000)));
        var agent = new AgentLoop(client, registry, new AgentLoopConfig
        {
            MaxContextCharacters = 1200
        });

        await agent.RunAsync("run the tool");

        var followUpRequest = client.Requests[1];
        var toolCallMessage = Assert.Single(followUpRequest.Messages.Where(message =>
            message.Role == "assistant" && message.ToolCalls.Count > 0));
        var toolMessage = Assert.Single(followUpRequest.Messages.Where(message => message.Role == "tool"));
        Assert.Equal(Assert.Single(toolCallMessage.ToolCalls).Id, toolMessage.ToolCallId);
        Assert.Contains("因上下文預算限制而截斷", toolMessage.Content);
        var estimatedSize = followUpRequest.Messages.Sum(message =>
            message.Content.Length + message.ToolCalls.Sum(call => call.Arguments.GetRawText().Length));
        Assert.True(estimatedSize <= 1200);
    }

    [Fact]
    public async Task AgentLoop_LoadsPersistedConversationOnlyOnce()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), $"superagent-{Guid.NewGuid():N}.json");
        try
        {
            var storage = new ConversationStorage(storagePath);
            var firstClient = new RecordingAgentClient();
            var firstAgent = new AgentLoop(firstClient, new ToolRegistry(), storage, "conversation-1");
            await firstAgent.RunAsync("first task");

            var resumedClient = new RecordingAgentClient();
            var resumedAgent = new AgentLoop(resumedClient, new ToolRegistry(), storage, "conversation-1");
            await resumedAgent.RunAsync("second task");

            var resumedRequest = Assert.Single(resumedClient.Requests);
            Assert.Single(resumedRequest.Messages.Where(message => message.Role == "system"));
            Assert.Equal(2, resumedRequest.Messages.Count(message => message.Role == "user"));
        }
        finally
        {
            if (File.Exists(storagePath))
            {
                File.Delete(storagePath);
            }
        }
    }

    private sealed class RecordingAgentClient(bool returnToolCallFirst = false) : IAgentClient
    {
        public List<AgentRequest> Requests { get; } = new();

        public Task<AgentResponse> GetResponseAsync(AgentRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (returnToolCallFirst && Requests.Count == 1)
            {
                return Task.FromResult(new AgentResponse
                {
                    ToolCalls =
                    {
                        new ToolCallDefinition
                        {
                            Id = "call-1",
                            Name = "fixed_result",
                            Arguments = JsonSerializer.SerializeToElement(new { })
                        }
                    }
                });
            }

            return Task.FromResult(new AgentResponse { Content = "done" });
        }
    }

    private sealed class FixedResultTool(string result = "tool result") : ITool
    {
        public string Name => "fixed_result";
        public string Description => "Returns a fixed test result.";
        public JsonElement Parameters => JsonSerializer.SerializeToElement(new { type = "object" });

        public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }
}
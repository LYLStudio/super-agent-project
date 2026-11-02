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
        Assert.True(config.EnableToolChoice);
    }
}
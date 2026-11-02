using Xunit;
using SuperAgent.Core;
using SuperAgent.Client;

namespace SuperAgent.Client.Tests;

public class AgentClientTests
{
    [Fact]
    public void OpenAICompatClient_CreatesSuccessfully()
    {
        var client = new OpenAICompatClient("http://localhost:8080", "test-key", "qwen3-coder:30b");
        Assert.NotNull(client);
    }

    [Fact]
    public void OpenAICompatClientFactory_CreatesClient()
    {
        var client = OpenAICompatClientFactory.Create("http://localhost:8080", "test-key");
        Assert.NotNull(client);
    }
}
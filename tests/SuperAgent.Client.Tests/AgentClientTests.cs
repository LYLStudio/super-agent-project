using Xunit;
using SuperAgent.Core;
using SuperAgent.Client;

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
}

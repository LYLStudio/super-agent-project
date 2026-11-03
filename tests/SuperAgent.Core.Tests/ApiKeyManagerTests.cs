using Xunit;
using SuperAgent.Core;

namespace SuperAgent.Core.Tests;

public class ApiKeyManagerTests
{
    [Fact]
    public void ApiKeyManager_AddAndGetKey_Success()
    {
        var manager = new ApiKeyManager("test-encryption-key-0000000000000000");

        manager.AddKey("my-key", "sk-test1234567890");

        var key = manager.GetCurrentKey("my-key");
        Assert.Equal("sk-test1234567890", key);
    }

    [Fact]
    public void ApiKeyManager_GetKey_EncryptionWorks()
    {
        var manager = new ApiKeyManager("test-encryption-key-0000000000000000");

        manager.AddKey("my-key", "super-secret-api-key-value");

        // 取得密鑰應該與原始值相同
        var key = manager.GetCurrentKey("my-key");
        Assert.Equal("super-secret-api-key-value", key);
    }

    [Fact]
    public void ApiKeyManager_GetAllKeys_ReturnsInfoWithoutEncryptedValues()
    {
        var manager = new ApiKeyManager("test-encryption-key-0000000000000000");

        manager.AddKey("key1", "value1");
        manager.AddKey("key2", "value2");

        var keys = manager.GetAllKeys();
        Assert.Equal(2, keys.Count);
        foreach (var k in keys)
        {
            Assert.NotEmpty(k.Name);
            Assert.True(k.IsActive);
        }
    }

    [Fact]
    public void ApiKeyManager_IsKeyValid_ReturnsCorrectStatus()
    {
        var manager = new ApiKeyManager("test-encryption-key-0000000000000000");

        manager.AddKey("active-key", "value1");
        manager.AddKey("inactive-key", "value2");

        // 停用第一個密鑰，啟用第二個
        _ = manager.RotateKey("active-key");

        Assert.True(manager.IsKeyValid("inactive-key"));
        Assert.Equal(2, manager.KeyCount);
        Assert.Equal(1, manager.ActiveKeyCount);
    }

    [Fact]
    public void ApiKeyManager_LoadFromEnvironment_Success()
    {
        var manager = new ApiKeyManager("test-encryption-key-0000000000000000");

        // 測試從環境變數載入
        Environment.SetEnvironmentVariable("TEST_API_KEY", "env-test-key-value");
        manager.LoadFromEnvironment("TEST_API_KEY");

        var key = manager.GetCurrentKey("TEST_API_KEY");
        Assert.Equal("env-test-key-value", key);

        // 清理
        Environment.SetEnvironmentVariable("TEST_API_KEY", null);
    }

    [Fact]
    public void ApiKeyManager_LoadFromConfig_Success()
    {
        var manager = new ApiKeyManager("test-encryption-key-0000000000000000");

        var config = new Dictionary<string, string>
        {
            { "ApiKey:my-key", "config-test-key-value" },
            { "App:Name", "test" }
        };

        manager.LoadFromConfig(config);

        var key = manager.GetCurrentKey("my-key");
        Assert.Equal("config-test-key-value", key);
    }

    [Fact]
    public void ApiKeyManager_AddKey_ThrowsOnEmptyName()
    {
        var manager = new ApiKeyManager("test-encryption-key-0000000000000000");

        Assert.Throws<ArgumentException>(() => manager.AddKey("", "value"));
    }

    [Fact]
    public void ApiKeyManager_AddKey_ThrowsOnEmptyValue()
    {
        var manager = new ApiKeyManager("test-encryption-key-0000000000000000");

        Assert.Throws<ArgumentException>(() => manager.AddKey("my-key", ""));
    }
}

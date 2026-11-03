using Xunit;
using SuperAgent.Core;
using System.Text.Json;
using System.Threading;

namespace SuperAgent.Core.Tests;

public class LoggingToolTests
{
    [Fact]
    public void LoggingTool_WritesInfoLog()
    {
        var tool = new LoggingTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            action = "write",
            level = "info",
            message = "測試日誌訊息",
            source = "TestModule"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.Contains("日誌已記錄", result);
    }

    [Fact]
    public void LoggingTool_WritesErrorLog()
    {
        var tool = new LoggingTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            action = "write",
            level = "error",
            message = "錯誤測試訊息",
            source = "ErrorModule"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.Contains("日誌已記錄", result);
    }

    [Fact]
    public void LoggingTool_QueriesLogs()
    {
        var tool = new LoggingTool();

        // Write some logs first
        tool.ExecuteAsync(JsonSerializer.SerializeToElement(new
        {
            action = "write",
            level = "info",
            message = "測試訊息一",
            source = "TestSource"
        }), CancellationToken.None).Wait();

        tool.ExecuteAsync(JsonSerializer.SerializeToElement(new
        {
            action = "write",
            level = "error",
            message = "錯誤測試訊息",
            source = "ErrorSource"
        }), CancellationToken.None).Wait();

        // Query logs
        var args = JsonSerializer.SerializeToElement(new
        {
            action = "query",
            query = "測試",
            limit = 10
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.Contains("日誌查詢結果", result);
    }

    [Fact]
    public void LoggingTool_ClearsLogs()
    {
        var tool = new LoggingTool();

        // Write a log
        tool.ExecuteAsync(JsonSerializer.SerializeToElement(new
        {
            action = "write",
            level = "info",
            message = "要清除的日誌",
            source = "TestSource"
        }), CancellationToken.None).Wait();

        // Clear logs
        var args = JsonSerializer.SerializeToElement(new
        {
            action = "clear"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.Contains("已清除", result);
    }

    [Fact]
    public void LoggingTool_GetLogLevelStats()
    {
        var tool = new LoggingTool();

        // Write logs of different levels
        tool.ExecuteAsync(JsonSerializer.SerializeToElement(new
        {
            action = "write",
            level = "info",
            message = "Info message",
            source = "Test"
        }), CancellationToken.None).Wait();

        tool.ExecuteAsync(JsonSerializer.SerializeToElement(new
        {
            action = "write",
            level = "error",
            message = "Error message",
            source = "Test"
        }), CancellationToken.None).Wait();

        // Get stats
        var args = JsonSerializer.SerializeToElement(new
        {
            action = "level"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.Contains("日誌級別統計", result);
    }

    [Fact]
    public void LoggingTool_HandlesUnknownAction()
    {
        var tool = new LoggingTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            action = "unknown_action"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.Contains("未知的日誌操作", result);
    }
}

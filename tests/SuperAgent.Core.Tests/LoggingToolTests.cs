using Xunit;
using SuperAgent.Core;
using System.Text.Json;
using System.Threading;
using System.IO;

namespace SuperAgent.Core.Tests;

public class LoggingToolTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), $"superagent-logs-{Guid.NewGuid():N}");

    public LoggingToolTests()
    {
        Directory.CreateDirectory(_logDirectory);
    }

    public void Dispose()
    {
        Directory.Delete(_logDirectory, recursive: true);
    }

    private LoggingTool CreateTool(
        bool enabled = true,
        string? path = null,
        LogRollingInterval interval = LogRollingInterval.Day,
        long fileSizeLimitBytes = 10 * 1024 * 1024)
    {
        return new LoggingTool(
            enabled,
            path ?? Path.Combine(_logDirectory, "superagent.log"),
            interval,
            fileSizeLimitBytes);
    }

    [Fact]
    public void LoggingTool_WritesInfoLog()
    {
        var tool = CreateTool();
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
        var tool = CreateTool();
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
        var tool = CreateTool();

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
        var tool = CreateTool();

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
        var tool = CreateTool();

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
        var tool = CreateTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            action = "unknown_action"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.Contains("未知的日誌操作", result);
    }

    [Fact]
    public async Task LoggingTool_TruncatesOversizedMessagesAndRejectsLargeQueries()
    {
        LoggingTool.ClearAllLogs();
        var tool = CreateTool();
        var writeResult = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new
        {
            action = "write",
            message = new string('x', 9000)
        }), CancellationToken.None);
        var queryResult = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new
        {
            action = "query",
            limit = 201
        }), CancellationToken.None);

        Assert.Contains("超出長度的內容已截斷", writeResult);
        Assert.Equal(8192, Assert.Single(LoggingTool.GetLogs()).Message.Length);
        Assert.Contains("limit 必須介於", queryResult);
    }

    [Fact]
    public async Task LoggingTool_WritesEnabledLogsToDailyFilesByDefault()
    {
        var tool = new LoggingTool(logFilePath: Path.Combine(_logDirectory, "superagent.log"));
        var args = JsonSerializer.SerializeToElement(new
        {
            action = "write",
            level = "info",
            message = "persistent log entry",
            source = "Test"
        });

        var result = await tool.ExecuteAsync(args, CancellationToken.None);
        var file = Assert.Single(Directory.GetFiles(_logDirectory));

        Assert.Contains("日誌已記錄", result);
        Assert.Matches(@"-\d{8}\.log$", file);
        Assert.Contains("persistent log entry", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task LoggingTool_RollsOverWhenFileSizeLimitIsReached()
    {
        var tool = CreateTool(fileSizeLimitBytes: 300);
        var args = JsonSerializer.SerializeToElement(new
        {
            action = "write",
            level = "info",
            message = new string('x', 100),
            source = "Test"
        });

        await tool.ExecuteAsync(args, CancellationToken.None);
        await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.Equal(2, Directory.GetFiles(_logDirectory).Length);
    }

    [Fact]
    public async Task LoggingTool_UsesHourlyFileNamesWhenConfigured()
    {
        var tool = CreateTool(interval: LogRollingInterval.Hour);
        var args = JsonSerializer.SerializeToElement(new
        {
            action = "write",
            message = "hourly log entry"
        });

        await tool.ExecuteAsync(args, CancellationToken.None);
        var file = Assert.Single(Directory.GetFiles(_logDirectory));

        Assert.Matches(@"-\d{8}-\d{2}\.log$", file);
    }

    [Fact]
    public async Task LoggingTool_DoesNotWriteWhenDisabled()
    {
        var tool = CreateTool(enabled: false);
        var args = JsonSerializer.SerializeToElement(new
        {
            action = "write",
            message = "should not persist"
        });

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.Contains("已停用", result);
        Assert.Empty(Directory.GetFiles(_logDirectory));
    }

    [Fact]
    public async Task LoggingTool_ReadsRecentLogsFromDiskAndAppliesFilter()
    {
        var writer = CreateTool();
        foreach (var message in new[] { "older entry", "matching entry", "newest entry" })
        {
            await writer.ExecuteAsync(JsonSerializer.SerializeToElement(new
            {
                action = "write",
                level = "info",
                message,
                source = "PersistentTest"
            }), CancellationToken.None);
        }

        var reader = CreateTool(enabled: false);
        var entries = reader.ReadRecentLogs(limit: 2);
        var filteredEntries = reader.ReadRecentLogs(limit: 10, query: "matching");

        Assert.Equal(new[] { "newest entry", "matching entry" }, entries.Select(entry => entry.Message));
        Assert.Equal("matching entry", Assert.Single(filteredEntries).Message);
    }

    [Fact]
    public async Task LoggingTool_ReadsSizeRolledFilesInNewestSegmentOrder()
    {
        var writer = CreateTool(fileSizeLimitBytes: 300);
        foreach (var message in new[] { "first segment", "second segment", "third segment" })
        {
            await writer.ExecuteAsync(JsonSerializer.SerializeToElement(new
            {
                action = "write",
                level = "info",
                message = new string(message[0], 100)
            }), CancellationToken.None);
        }

        var entries = CreateTool(enabled: false).ReadRecentLogs(limit: 3);

        Assert.Equal(3, entries.Count);
        Assert.Equal("t", entries[0].Message[..1]);
        Assert.Equal("s", entries[1].Message[..1]);
        Assert.Equal("f", entries[2].Message[..1]);
    }

    [Fact]
    public void LoggingTool_WritesApplicationEventsWithoutConsoleEcho()
    {
        var tool = CreateTool();

        var result = tool.WriteApplicationEvent("info", "Application started", "SuperAgent");
        var entries = tool.ReadRecentLogs();

        Assert.Contains("日誌已記錄", result);
        Assert.Equal("Application started", Assert.Single(entries).Message);
    }

    [Fact]
    public void LoggingTool_DoesNotWriteApplicationEventsWhenDisabled()
    {
        var tool = CreateTool(enabled: false);

        var result = tool.WriteApplicationEvent("info", "Application started", "SuperAgent");

        Assert.Contains("已停用", result);
        Assert.Empty(Directory.GetFiles(_logDirectory));
    }
}

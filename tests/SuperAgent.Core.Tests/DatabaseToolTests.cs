using Xunit;
using SuperAgent.Core;
using System.Text.Json;
using System.Threading;

namespace SuperAgent.Core.Tests;

public class DatabaseToolTests
{
    [Fact]
    public void DatabaseTool_ExecutesQuery()
    {
        var tool = new DatabaseTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            connection_string = "Data Source=test.db",
            query = "SELECT * FROM Users"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void DatabaseTool_HandlesEmptyQuery()
    {
        var tool = new DatabaseTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            connection_string = "Data Source=test.db",
            query = ""
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        var hasError = result.Contains("錯誤") || result.Contains("查詢語句不能為空");
        Assert.True(hasError);
    }

    [Fact]
    public void DatabaseTool_ShowsColumnInfo()
    {
        var tool = new DatabaseTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            connection_string = "Data Source=test.db",
            query = "SELECT Id, Name, Email FROM Users"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public async Task DatabaseTool_FormatsNonStringValuesAndLimitsRows()
    {
        var tool = new DatabaseTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            connection_string = "Data Source=:memory:",
            query = "SELECT 1 AS number, NULL AS null_value UNION ALL SELECT 2, NULL UNION ALL SELECT 3, NULL",
            max_rows = 2
        });

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.True(result.Contains("2 row(s) returned"), result);
        Assert.Contains("\"number\":\"1\"", result);
        Assert.Contains("\"null_value\":\"NULL\"", result);
        Assert.Contains("Results truncated", result);
    }

    [Fact]
    public async Task DatabaseTool_RejectsInvalidOutputLimits()
    {
        var tool = new DatabaseTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            connection_string = "Data Source=:memory:",
            query = "SELECT 1",
            max_rows = 0
        });

        var result = await tool.ExecuteAsync(args, CancellationToken.None);

        Assert.Contains("max_rows 必須介於", result);
    }
}

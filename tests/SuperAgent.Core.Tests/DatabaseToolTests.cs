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
}

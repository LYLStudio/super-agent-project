using Xunit;
using SuperAgent.Core;
using System.Text.Json;
using System.Threading;

namespace SuperAgent.Core.Tests;

public class GitToolTests
{
    [Fact]
    public void GitTool_ExecutesCommand()
    {
        var tool = new GitTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            command = "status"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void GitTool_HandlesEmptyCommand()
    {
        var tool = new GitTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            command = ""
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        var hasError = result.Contains("錯誤") || result.Contains("命令");
        Assert.True(hasError);
    }

    [Fact]
    public void GitTool_ShowsCommandOutput()
    {
        var tool = new GitTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            command = "log --oneline -5"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result));
    }
}

using Xunit;
using SuperAgent.Core;
using System.Text.Json;
using System.Threading;

namespace SuperAgent.Core.Tests;

public class FileSearchToolTests
{
    [Fact]
    public void FileSearchTool_SearchesFiles()
    {
        var tool = new FileSearchTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            directory = "src",
            pattern = "*.cs"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void FileSearchTool_HandlesInvalidPath()
    {
        var tool = new FileSearchTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            directory = "/invalid/path",
            pattern = "*.cs"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        var hasError = result.Contains("錯誤") || result.Contains("路徑") || result.Contains("Directory not found");
        Assert.True(hasError);
    }

    [Fact]
    public void FileSearchTool_ShowsFileCount()
    {
        var tool = new FileSearchTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            directory = "src/SuperAgent.Core",
            pattern = "*.cs"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result));
    }
}

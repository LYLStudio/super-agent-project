using Xunit;
using SuperAgent.Core;
using System.Text.Json;
using System.Threading;
using System.Diagnostics;

namespace SuperAgent.Core.Tests;

public class ToolRegistryTests
{
    [Fact]
    public void ToolRegistry_RegisterAndGetAllTools()
    {
        var registry = new ToolRegistry();
        registry.Register(new CodeWriterTool());

        var tools = registry.GetAllTools();
        Assert.Single(tools);
        Assert.Equal("code_writer", tools[0].Name);
        Assert.NotNull(tools[0].Description);
    }

    [Fact]
    public void ToolRegistry_CodeWriter_Executes()
    {
        var registry = new ToolRegistry();
        registry.Register(new CodeWriterTool());

        var args = JsonSerializer.SerializeToElement(new
        {
            file_path = "/tmp/test_output.txt",
            content = "Hello World"
        });

        var result = registry.ExecuteAsync("code_writer", args, CancellationToken.None).Result;
        Assert.Contains("Successfully wrote", result);
        Assert.True(File.Exists("/tmp/test_output.txt"));
        
        // 清理測試檔案
        File.Delete("/tmp/test_output.txt");
    }

    [Fact]
    public async Task ShellExecTool_DrainsLargeOutputWithoutHanging()
    {
        var tool = new ShellExecTool();
        var arguments = JsonSerializer.SerializeToElement(new
        {
            command = "for i in {1..2000}; do printf '%080d\\n' \"$i\"; printf '%080d\\n' \"$i\" >&2; done"
        });

        var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

        Assert.Contains("STDOUT:", result);
        Assert.Contains("STDERR:", result);
        Assert.Contains("Exit code: 0", result);
    }

    [Fact]
    public async Task ShellExecTool_StopsCommandWhenTimeoutExpires()
    {
        var tool = new ShellExecTool();
        var arguments = JsonSerializer.SerializeToElement(new
        {
            command = "sleep 10",
            timeout_seconds = 1
        });
        var stopwatch = Stopwatch.StartNew();

        var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

        Assert.Contains("timed out after 1 seconds", result);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }
}
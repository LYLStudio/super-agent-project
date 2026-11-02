using Xunit;
using SuperAgent.Core;
using System.Text.Json;
using System.Threading;

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
}
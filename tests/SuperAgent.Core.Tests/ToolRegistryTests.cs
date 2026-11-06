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
        tools[0].Name = "modified";
        Assert.Equal("code_writer", Assert.Single(registry.GetAllTools()).Name);
    }

    [Fact]
    public async Task ToolRegistry_UnregisterDisablesToolAndRemovesItsDefinition()
    {
        var registry = new ToolRegistry();
        registry.Register(new CodeWriterTool());

        Assert.True(registry.Unregister("code_writer"));
        Assert.Empty(registry.GetAllTools());

        var arguments = JsonSerializer.SerializeToElement(new
        {
            file_path = "unused.txt",
            content = "unused"
        });
        var result = await registry.ExecuteAsync("code_writer", arguments, CancellationToken.None);

        Assert.Contains("未找到", result);
        Assert.False(registry.Unregister("code_writer"));
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
    public async Task ToolRegistry_ConsumesToolResultsWithoutBackgroundWork()
    {
        var registry = new ToolRegistry();
        registry.SetResult("tool-call", "result");

        var resultTask = registry.GetLastResultAsync("tool-call");

        Assert.True(resultTask.IsCompleted);
        Assert.Equal("result", await resultTask);
        Assert.Null(await registry.GetLastResultAsync("tool-call"));
    }

    [Fact]
    public async Task ToolRegistry_BoundsUnconsumedToolResults()
    {
        var registry = new ToolRegistry();
        for (var index = 0; index < 257; index++)
        {
            registry.SetResult($"call-{index}", $"result-{index}");
        }

        Assert.Null(await registry.GetLastResultAsync("call-0"));
        Assert.Equal("result-256", await registry.GetLastResultAsync("call-256"));
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

    [Fact]
    public async Task ShellExecTool_RunsInWorkspaceSubdirectoryByDefault()
    {
        var workspace = Directory.CreateTempSubdirectory().FullName;
        var subdirectory = Directory.CreateDirectory(Path.Combine(workspace, "src")).FullName;
        try
        {
            var tool = new ShellExecTool(
                accessPolicy: new WorkspaceAccessPolicy(workspace, allowOutsideWorkspace: false));
            var arguments = JsonSerializer.SerializeToElement(new
            {
                command = "pwd",
                working_directory = "src"
            });

            var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

            Assert.Contains(subdirectory, result);
            Assert.Contains("Exit code: 0", result);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task ShellExecTool_DeniesOutsideWorkingDirectoryByDefault()
    {
        var workspace = Directory.CreateTempSubdirectory().FullName;
        var outside = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var tool = new ShellExecTool(
                accessPolicy: new WorkspaceAccessPolicy(workspace, allowOutsideWorkspace: false));
            var arguments = JsonSerializer.SerializeToElement(new
            {
                command = "pwd",
                working_directory = outside
            });

            var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

            Assert.Contains("outside the configured workspace", result);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task GitTool_RejectsWorkingDirectoryOverrideOptions()
    {
        var workspace = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var tool = new GitTool(
                accessPolicy: new WorkspaceAccessPolicy(workspace, allowOutsideWorkspace: false));
            var arguments = JsonSerializer.SerializeToElement(new
            {
                command = "-C /tmp status"
            });

            var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

            Assert.Contains("path override options", result);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
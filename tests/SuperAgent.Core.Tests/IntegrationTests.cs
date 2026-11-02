using Xunit;
using SuperAgent.Core;
using System.Text.Json;
using System.Threading;
using System.IO;

namespace SuperAgent.Core.Tests;

public class IntegrationTests
{
    [Fact]
    public async Task AgentLoop_ExecutesWithoutTools()
    {
        // 建立 Mock 客戶端
        var mockClient = new IntegrationMockClient();
        var registry = new ToolRegistry();
        
        // 註冊預設工具
        registry.Register(new CodeWriterTool());
        registry.Register(new CodeReaderTool());
        registry.Register(new ShellExecTool());

        var agent = new AgentLoop(mockClient, registry);

        // 執行不需要工具的任務
        var result = await agent.RunAsync("請告訴我今天的日期", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(AgentLoopState.Completed, result.FinalState);
        Assert.NotEmpty(result.FinalContent);
        // 應該至少執行一次迭代
        Assert.True(result.TotalIterations >= 1);
    }

    [Fact]
    public async Task AgentLoop_ExecutesWithTools()
    {
        // 建立 Mock 客戶端
        var mockClient = new IntegrationMockClient
        {
            ShouldExecuteTool = true
        };
        var registry = new ToolRegistry();
        
        // 註冊預設工具
        registry.Register(new CodeWriterTool());
        registry.Register(new CodeReaderTool());

        var agent = new AgentLoop(mockClient, registry);

        // 執行需要工具的任務
        var result = await agent.RunAsync("請在 /tmp/test_integration.txt 裡寫入 Hello Integration", CancellationToken.None);

        Assert.True(result.Success);
        // 驗證文件是否被寫入
        Assert.True(File.Exists("/tmp/test_integration.txt"));
        var content = await File.ReadAllTextAsync("/tmp/test_integration.txt");
        Assert.Contains("Hello Integration", content);
        
        // 清理測試檔案
        File.Delete("/tmp/test_integration.txt");
    }

    [Fact]
    public async Task AgentLoop_HandlesErrorGracefully()
    {
        // 建立會拋出例外的 Mock 客戶端
        var mockClient = new IntegrationMockClient
        {
            ShouldFail = true
        };
        var registry = new ToolRegistry();
        
        var agent = new AgentLoop(mockClient, registry);

        // 執行任務，預期 Agent Loop 能優雅處理錯誤
        var result = await agent.RunAsync("測試錯誤處理", CancellationToken.None);

        // 應該能處理錯誤而不崩潰
        Assert.NotNull(result);
    }
}

/// <summary>
/// 用於整合測試的 Mock 客戶端
/// </summary>
public class IntegrationMockClient : IAgentClient
{
    public bool ShouldFail { get; set; } = false;
    public bool ShouldExecuteTool { get; set; } = false;

    public Task<AgentResponse> GetResponseAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        if (ShouldFail)
        {
            return Task.FromException<AgentResponse>(new InvalidOperationException("Mock API error"));
        }

        // 檢查是否有工具呼叫需求
        var hasToolCalls = request.Messages.Any(m => m.ToolCalls.Count > 0);
        
        var response = new AgentResponse
        {
            Content = $"Mock response to: {request.Messages.LastOrDefault()?.Content ?? "empty"}"
        };

        // 模擬需要工具執行的情況
        if (request.Messages.LastOrDefault()?.Content.Contains("寫入", StringComparison.OrdinalIgnoreCase) == true 
            && !hasToolCalls
            && ShouldExecuteTool)
        {
            response.ToolCalls.Add(new ToolCallDefinition
            {
                Id = "tool_1",
                Name = "code_writer",
                Arguments = JsonSerializer.SerializeToElement(new
                {
                    file_path = "/tmp/test_integration.txt",
                    content = "Hello Integration"
                })
            });
        }

        return Task.FromResult(response);
    }
}
using Xunit;
using SuperAgent.Core;
using System.Text.Json;
using System.Threading;

namespace SuperAgent.Core.Tests;

public class DebuggerToolTests
{
    [Fact]
    public void Debugger_AnalyzesNullReferenceError()
    {
        var tool = new DebuggerTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            errorMessage = "NullReferenceException: 空值參照錯誤",
            language = "csharp"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.Contains("NullReferenceException", result);
        Assert.Contains("修復建議", result);
    }

    [Fact]
    public void Debugger_AnalyzesFileNotFoundError()
    {
        var tool = new DebuggerTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            errorMessage = "FileNotFoundException: 檔案未找到",
            language = "csharp"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.Contains("FileNotFoundException", result);
    }

    [Fact]
    public void Debugger_AnalyzesTimeoutError()
    {
        var tool = new DebuggerTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            errorMessage = "TimeoutException: 超時錯誤",
            language = "csharp"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.Contains("TimeoutException", result);
    }

    [Fact]
    public void Debugger_HandlesUnknownError()
    {
        var tool = new DebuggerTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            errorMessage = "Some unknown error occurred",
            language = "csharp"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.Contains("UnknownError", result);
        Assert.Contains("除錯步驟", result);
    }

    [Fact]
    public void Debugger_AnalyzesWithStackTrace()
    {
        var tool = new DebuggerTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            errorMessage = "NullReferenceException",
            stackTrace = "at MyClass.Method() at Program.Main()",
            language = "csharp"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.Contains("堆疊追蹤分析", result);
        Assert.Contains("MyClass.Method", result);
    }
}

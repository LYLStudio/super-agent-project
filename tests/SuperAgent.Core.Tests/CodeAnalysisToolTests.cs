using Xunit;
using SuperAgent.Core;
using System.Text.Json;
using System.Threading;

namespace SuperAgent.Core.Tests;

public class CodeAnalysisToolTests
{
    [Fact]
    public void CodeAnalysisTool_AnalyzesCSharpCode()
    {
        var tool = new CodeAnalysisTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            code = "public class Test { public void Method() { int x = 1; } }",
            language = "csharp",
            analysisType = "quality"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.Contains("程式碼品質分析結果", result);
        Assert.Contains("csharp", result);
    }

    [Fact]
    public void CodeAnalysisTool_DetectsLongLines()
    {
        var tool = new CodeAnalysisTool();
        var longLine = string.Join("", Enumerable.Range(1, 150).Select(i => i.ToString()));
        var args = JsonSerializer.SerializeToElement(new
        {
            code = longLine,
            language = "csharp"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.Contains("行長過長", result);
    }

    [Fact]
    public void CodeAnalysisTool_DetectsTodoComments()
    {
        var tool = new CodeAnalysisTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            code = "// TODO: 這裡需要實作\npublic void Method() { }",
            language = "csharp"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.Contains("待處理註解", result);
    }

    [Fact]
    public void CodeAnalysisTool_ReturnsImprovementSuggestions()
    {
        var tool = new CodeAnalysisTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            code = "public class Test { }",
            language = "csharp"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.Contains("改善建議", result);
    }

    [Fact]
    public void CodeAnalysisTool_HandlesEmptyCode()
    {
        var tool = new CodeAnalysisTool();
        var args = JsonSerializer.SerializeToElement(new
        {
            code = "",
            language = "csharp"
        });

        var result = tool.ExecuteAsync(args, CancellationToken.None).Result;
        Assert.NotNull(result);
        Assert.Contains("程式碼品質分析結果", result);
    }
}

using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SuperAgent.Core;

namespace SuperAgent.Core;

/// <summary>
/// TestGeneratorTool - 自動產生單元測試工具
/// 根據程式碼自動產生對應的單元測試
/// </summary>
public class TestGeneratorTool : ITool
{
    public string Name => "test_generator";
    public string Description => "根據程式碼自動產生單元測試代碼";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            code = new { type = "string", description = "要產生測試的程式碼" },
            language = new { type = "string", description = "程式語言 (csharp, javascript, python, etc.)" },
            testFramework = new { type = "string", description = "測試框架 (xunit, nunit, mstest)" }
        },
        required = new[] { "code", "language" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var code = GetStringProperty(arguments, "code") ?? string.Empty;
        var language = GetStringProperty(arguments, "language") ?? "csharp";
        var testFramework = GetStringProperty(arguments, "testFramework") ?? "xunit";

        var result = GenerateTests(code, language, testFramework);
        return result;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }

        return null;
    }

    private string GenerateTests(string code, string language, string testFramework)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "程式碼為空，無法產生測試。";
        }

        if (!language.Equals("csharp", StringComparison.OrdinalIgnoreCase))
        {
            return "目前僅支援 C# 程式碼的測試產生。程式碼:\n" + code;
        }

        var className = ExtractClassName(code);
        var methodNames = ExtractMethodNames(code);

        var testCode = "using System;\nusing Xunit;\nusing System.Threading;\nusing System.Threading.Tasks;\n\nnamespace SuperAgent.Core.Tests;\n\npublic class " + className + "Tests\n{\n";

        // 為每個方法產生測試
        foreach (var methodName in methodNames)
        {
            testCode += "    [Fact]\n    public void " + methodName + "_Test()\n    {\n        // Arrange\n        var " + className + " = new " + className + "();\n\n        // Act\n        // TODO: 請根據方法簽名填入正確的參數\n        // var result = " + className + "." + methodName + "();\n\n        // Assert\n        // TODO: 請根據預期結果填入斷言\n        // Assert.True(result);\n    }\n\n";
        }

        testCode += "}\n";

        return testCode;
    }

    private string ExtractClassName(string code)
    {
        var match = Regex.Match(code, @"class\s+(\w+)");
        return match.Success ? match.Groups[1].Value : "UnknownClass";
    }

    private List<string> ExtractMethodNames(string code)
    {
        var methods = new List<string>();
        var pattern = @"(?:public|private|protected|internal)\s+(?:static\s+)?[\w<>]+\s+(\w+)\s*\(";
        var matches = Regex.Matches(code, pattern);
        foreach (Match match in matches)
        {
            methods.Add(match.Groups[1].Value);
        }
        return methods;
    }
}

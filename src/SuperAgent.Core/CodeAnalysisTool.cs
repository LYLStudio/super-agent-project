using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SuperAgent.Core;

namespace SuperAgent.Core;

/// <summary>
/// CodeAnalysisTool - 程式碼分析工具
/// 分析程式碼品質與潛在問題
/// </summary>
public class CodeAnalysisTool : ITool
{
    public string Name => "code_analyzer";
    public string Description => "分析程式碼品質，檢測潛在問題並提供改善建議";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            code = new { type = "string", description = "要分析的程式碼" },
            language = new { type = "string", description = "程式語言 (csharp, javascript, python, etc.)" },
            analysisType = new { type = "string", description = "分析類型 (quality, security, performance, all)" }
        },
        required = new[] { "code", "language" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var code = GetStringProperty(arguments, "code") ?? string.Empty;
        var language = GetStringProperty(arguments, "language") ?? "csharp";
        var analysisType = GetStringProperty(arguments, "analysisType") ?? "all";

        var results = new List<string>();

        // 基本程式碼品質檢查
        results.Add("=== 程式碼品質分析結果 ===");
        results.Add($"程式語言: {language}");
        results.Add($"程式碼長度: {code.Length} 字元");
        results.Add($"行數: {code.Split('\n').Length} 行");
        results.Add("");

        // 檢查常見問題
        var issues = AnalyzeCodeQuality(code, language);

        if (issues.Count > 0)
        {
            results.Add("=== 發現的問題 ===");
            foreach (var issue in issues)
            {
                results.Add($"⚠️ {issue}");
            }
            results.Add("");
        }
        else
        {
            results.Add("✅ 未發現明顯品質問題");
            results.Add("");
        }

        // 提供改善建議
        var suggestions = GetImprovementSuggestions(code, language);
        if (suggestions.Count > 0)
        {
            results.Add("=== 改善建議 ===");
            foreach (var suggestion in suggestions)
            {
                results.Add($"💡 {suggestion}");
            }
        }

        return string.Join("\n", results);
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

    private List<string> AnalyzeCodeQuality(string code, string language)
    {
        var issues = new List<string>();
        var lines = code.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var lineNumber = i + 1;

            // 檢查過長行
            if (line.Length > 120)
            {
                issues.Add($"第 {lineNumber} 行: 行長過長 ({line.Length} 字元 > 120)");
            }

            // 檢查未使用變數 (簡單檢查)
            if (language.Equals("csharp", StringComparison.OrdinalIgnoreCase))
            {
                // 檢查空方法
                if (line.Contains("void ") && line.Contains("{") && line.Contains("}"))
                {
                    var methodBody = line.Substring(line.IndexOf('{') + 1, line.IndexOf('}') - line.IndexOf('{') - 1).Trim();
                    if (string.IsNullOrEmpty(methodBody))
                    {
                        issues.Add($"第 {lineNumber} 行: 空方法建議添加實作");
                    }
                }

                // 檢查硬編碼字符串
                if (line.Contains("\"") && line.Contains("=") && !line.Contains("//"))
                {
                    var strMatch = Regex.Match(line, "\"[^\"]+\"");
                    if (strMatch.Success && strMatch.Value.Length > 10)
                    {
                        issues.Add($"第 {lineNumber} 行: 發現長字符串常數，建議提取為變數");
                    }
                }
            }

            // 檢查 TODO/FIXME
            if (line.Contains("TODO") || line.Contains("FIXME"))
            {
                issues.Add($"第 {lineNumber} 行: 發現待處理註解 ({line.Trim()})");
            }
        }

        // 檢查整體結構
        if (language.Equals("csharp", StringComparison.OrdinalIgnoreCase))
        {
            // 檢查命名空間
            if (!code.Contains("namespace"))
            {
                issues.Add("建議：程式碼應包含命名空間定義");
            }

            // 檢查 using 陳述句
            if (!code.Contains("using"))
            {
                issues.Add("建議：檢查是否缺少必要的 using 陳述句");
            }
        }

        return issues;
    }

    private List<string> GetImprovementSuggestions(string code, string language)
    {
        var suggestions = new List<string>();

        if (language.Equals("csharp", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add("考慮使用 nullable 類型提升型別安全性");
            suggestions.Add("檢查是否有適當的錯誤處理 (try-catch)");
            suggestions.Add("考慮將大型方法拆分為較小的單一職責方法");
        }

        suggestions.Add("編寫單元測試以確保程式碼正確性");
        suggestions.Add("添加適當的日誌記錄以便除錯");

        return suggestions;
    }
}

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
/// RefactoringTool - 程式碼重構建議工具
/// 分析程式碼並提供重構建議
/// </summary>
public class RefactoringTool : ITool
{
    public string Name => "refactoring";
    public string Description => "分析程式碼並提供重構建議，包括命名改善、方法抽取、類別拆分等";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            code = new { type = "string", description = "要分析的程式碼" },
            language = new { type = "string", description = "程式語言 (csharp, javascript, python, etc.)" },
            refactoringType = new { type = "string", description = "重構類型 (naming, extract-method, split-class, all)" }
        },
        required = new[] { "code", "language" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var code = GetStringProperty(arguments, "code") ?? string.Empty;
        var language = GetStringProperty(arguments, "language") ?? "csharp";
        var refactoringType = GetStringProperty(arguments, "refactoringType") ?? "all";

        var result = AnalyzeCode(code, language, refactoringType);
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

    private string AnalyzeCode(string code, string language, string refactoringType)
    {
        var lines = new List<string>();
        lines.Add("=== 程式碼重構分析結果 ===");
        lines.Add($"程式語言: {language}");
        lines.Add($"程式碼長度: {code.Length} 字元");
        lines.Add($"行數: {code.Split('\n').Length} 行");
        lines.Add("");

        // 檢查各種重構機會
        var suggestions = new List<string>();

        if (refactoringType.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            refactoringType.Equals("naming", StringComparison.OrdinalIgnoreCase))
        {
            var namingSuggestions = CheckNamingIssues(code, language);
            suggestions.AddRange(namingSuggestions);
        }

        if (refactoringType.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            refactoringType.Equals("extract-method", StringComparison.OrdinalIgnoreCase))
        {
            var methodSuggestions = CheckExtractMethodOpportunities(code, language);
            suggestions.AddRange(methodSuggestions);
        }

        if (refactoringType.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            refactoringType.Equals("split-class", StringComparison.OrdinalIgnoreCase))
        {
            var classSuggestions = CheckSplitClassOpportunities(code, language);
            suggestions.AddRange(classSuggestions);
        }

        // 輸出結果
        if (suggestions.Count > 0)
        {
            lines.Add("=== 發現的重構機會 ===");
            for (int i = 0; i < suggestions.Count; i++)
            {
                lines.Add($"{i + 1}. {suggestions[i]}");
            }
            lines.Add("");
        }
        else
        {
            lines.Add("✅ 未發現明顯的重構機會");
            lines.Add("");
        }

        // 一般重構建議
        lines.Add("=== 一般重構建議 ===");
        lines.Add("1. 將大型方法拆分為較小的單一職責方法");
        lines.Add("2. 改善變數與方法命名，使其更具描述性");
        lines.Add("3. 移除重複程式碼，提取為共用方法");
        lines.Add("4. 將過大的類別拆分為多個職責明確的類別");
        lines.Add("5. 添加適當的註解與說明");
        lines.Add("6. 檢查是否有不必要的依賴");

        return string.Join("\n", lines);
    }

    private List<string> CheckNamingIssues(string code, string language)
    {
        var suggestions = new List<string>();
        var lines = code.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var lineNumber = i + 1;

            if (language.Equals("csharp", StringComparison.OrdinalIgnoreCase))
            {
                // 檢查單字母變數名
                if (Regex.IsMatch(line, @"\b[a-z]\b"))
                {
                    suggestions.Add($"第 {lineNumber} 行: 發現單字母變數名，建議改為更描述性的名稱");
                }

                // 檢查拼音變數名
                if (Regex.IsMatch(line, @"\b[a-z]{2,}\d+\b") && !line.Contains("//"))
                {
                    suggestions.Add($"第 {lineNumber} 行: 發現可能是拼音的變數名，建議改為英文描述性名稱");
                }

                // 檢查方法名過長
                var methodMatch = Regex.Match(line, @"\w+\(\w*\)");
                if (methodMatch.Success && methodMatch.Value.Length > 30)
                {
                    suggestions.Add($"第 {lineNumber} 行: 方法名過長 ({methodMatch.Value.Length} 字元)，建議縮短");
                }
            }
        }

        return suggestions;
    }

    private List<string> CheckExtractMethodOpportunities(string code, string language)
    {
        var suggestions = new List<string>();
        var lines = code.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // 檢查過長行
            if (line.Length > 120)
            {
                suggestions.Add($"第 {i + 1} 行: 行長過長 ({line.Length} 字元 > 120)，建議拆分為多個方法");
            }

            // 檢查複雜邏輯 (簡單檢查：多個 if 或括號)
            if (language.Equals("csharp", StringComparison.OrdinalIgnoreCase))
            {
                var ifCount = Regex.Matches(line, @"\bif\b").Count;
                if (ifCount >= 2)
                {
                    suggestions.Add($"第 {i + 1} 行: 發現多個條件判斷，建議提取為獨立方法");
                }
            }
        }

        return suggestions;
    }

    private List<string> CheckSplitClassOpportunities(string code, string language)
    {
        var suggestions = new List<string>();

        if (language.Equals("csharp", StringComparison.OrdinalIgnoreCase))
        {
            // 檢查類別是否過大
            var classCount = Regex.Matches(code, @"class\s+\w+").Count;
            if (classCount > 1)
            {
                suggestions.Add("檢測到多個類別，檢查是否有單一職責原則違反");
            }

            // 檢查方法數量
            var methodCount = Regex.Matches(code, @"(?:public|private|protected|internal)\s+(?:static\s+)?[\w<>]+\s+\w+\s*\(").Count;
            if (methodCount > 10)
            {
                suggestions.Add($"類別包含 {methodCount} 個方法，超過建議數量 (10)，建議拆分為多個類別");
            }

            // 檢查是否有多個職責
            if (code.Contains("Save") && code.Contains("Load") && code.Contains("Validate"))
            {
                suggestions.Add("檢測到多個職責 (儲存、載入、驗證)，建議拆分為多個類別");
            }
        }

        return suggestions;
    }
}

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
/// 分析程式碼品質與潛在問題，支援多語言與插件化
/// </summary>
public class CodeAnalysisTool : ITool
{
    public string Name => "code_analyzer";
    public string Description => "分析程式碼品質，檢測潛在問題並提供改善建議，支援多語言程式碼分析";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            code = new { type = "string", description = "要分析的程式碼" },
            language = new { type = "string", description = "程式語言 (csharp, javascript, python, java, etc.)" },
            analysisType = new { type = "string", description = "分析類型 (quality, security, performance, style, all)" }
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

        // 根據語言與分析類型執行檢查
        var issues = AnalyzeCodeQuality(code, language, analysisType);

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

    private List<string> AnalyzeCodeQuality(string code, string language, string analysisType)
    {
        var issues = new List<string>();
        var lowerLang = language.ToLowerInvariant();
        var lines = code.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var lineNumber = i + 1;

            // 檢查過長行 (通用)
            if (line.Length > 120)
            {
                issues.Add($"第 {lineNumber} 行: 行長過長 ({line.Length} 字元 > 120)");
            }

            // 檢查 TODO/FIXME (通用)
            if (line.Contains("TODO") || line.Contains("FIXME"))
            {
                issues.Add($"第 {lineNumber} 行: 發現待處理註解 ({line.Trim()})");
            }

            // 語言特定檢查
            if (lowerLang.Equals("csharp"))
            {
                AnalyzeCSharp(code, lines, issues, analysisType);
            }
            else if (lowerLang.Equals("javascript") || lowerLang.Equals("typescript"))
            {
                AnalyzeJavaScript(code, lines, issues, analysisType);
            }
            else if (lowerLang.Equals("python"))
            {
                AnalyzePython(code, lines, issues, analysisType);
            }
            else if (lowerLang.Equals("java"))
            {
                AnalyzeJava(code, lines, issues, analysisType);
            }
        }

        // 安全性檢查
        if (analysisType.Equals("security", StringComparison.OrdinalIgnoreCase) ||
            analysisType.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var securityIssues = AnalyzeSecurity(code, language);
            issues.AddRange(securityIssues);
        }

        // 效能檢查
        if (analysisType.Equals("performance", StringComparison.OrdinalIgnoreCase) ||
            analysisType.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var perfIssues = AnalyzePerformance(code, language);
            issues.AddRange(perfIssues);
        }

        // 樣式檢查
        if (analysisType.Equals("style", StringComparison.OrdinalIgnoreCase) ||
            analysisType.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var styleIssues = AnalyzeStyle(code, language);
            issues.AddRange(styleIssues);
        }

        return issues;
    }

    private void AnalyzeCSharp(string code, string[] lines, List<string> issues, string analysisType)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var lineNumber = i + 1;

            // 檢查空方法
            if (line.Contains("void ") && line.Contains("{") && line.Contains("}"))
            {
                var methodBody = line.Substring(line.IndexOf('{') + 1, line.IndexOf('}') - line.IndexOf('{') - 1).Trim();
                if (string.IsNullOrEmpty(methodBody))
                {
                    issues.Add($"第 {lineNumber} 行: 空方法建議添加實作");
                }
            }

            // 檢查硬編碼長字符串
            if (line.Contains("\"") && line.Contains("=") && !line.Contains("//"))
            {
                var strMatch = Regex.Match(line, "\"[^\"]+\"");
                if (strMatch.Success && strMatch.Value.Length > 20)
                {
                    issues.Add($"第 {lineNumber} 行: 發現長字符串常數，建議提取為變數或常數");
                }
            }

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

            // 檢查可 null 型別
            if (line.Contains("string ") && !line.Contains("?") && line.Contains("="))
            {
                issues.Add($"第 {lineNumber} 行: 考慮使用 nullable 類型 (string?) 提升型別安全性");
            }
        }
    }

    private void AnalyzeJavaScript(string code, string[] lines, List<string> issues, string analysisType)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var lineNumber = i + 1;

            // 檢查 var 使用 (建議使用 let/const)
            if (Regex.IsMatch(line, @"\bvar\s+"))
            {
                issues.Add($"第 {lineNumber} 行: 建議使用 let 或 const 代替 var");
            }

            // 檢查等號相等 (建議使用 ===)
            if (line.Contains("==") && !line.Contains("===") && !line.Contains("//"))
            {
                issues.Add($"第 {lineNumber} 行: 建議使用 === 嚴格相等代替 ==");
            }

            // 檢查分號遺漏
            if (!line.EndsWith(";") && !line.EndsWith("{") && !line.EndsWith("}") &&
                !line.Trim().StartsWith("//") && line.Length > 0 &&
                !line.Trim().StartsWith("if") && !line.Trim().StartsWith("for") &&
                !line.Trim().StartsWith("while") && !line.Trim().StartsWith("function"))
            {
                if (line.Contains("=") || line.Contains("return") || line.Contains("console.log"))
                {
                    issues.Add($"第 {lineNumber} 行: 建議添加分號");
                }
            }
        }
    }

    private void AnalyzePython(string code, string[] lines, List<string> issues, string analysisType)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var lineNumber = i + 1;

            // 檢查未使用的 import
            if (line.StartsWith("import ") || line.StartsWith("from "))
            {
                var parts = line.Split(' ');
                if (parts.Length > 1)
                {
                    var module = parts[1];
                    if (!code.Contains(module + ".") && !code.Contains(module + "("))
                    {
                        issues.Add($"第 {lineNumber} 行: 可能未使用的 import: {module}");
                    }
                }
            }

            // 檢查缺少 docstring
            if (line.StartsWith("def ") && i + 1 < lines.Length &&
                !lines[i + 1].Trim().StartsWith('"') && !lines[i + 1].Trim().StartsWith("'"))
            {
                issues.Add($"第 {lineNumber} 行: 建議為函數添加 docstring");
            }
        }
    }

    private void AnalyzeJava(string code, string[] lines, List<string> issues, string analysisType)
    {
        // 檢查命名空間 (package)
        if (!code.Contains("package"))
        {
            issues.Add("建議：Java 程式碼應包含 package 陳述句");
        }

        // 檢查 import
        if (!code.Contains("import"))
        {
            issues.Add("建議：檢查是否缺少必要的 import 陳述句");
        }
    }

    private List<string> AnalyzeSecurity(string code, string language)
    {
        var issues = new List<string>();
        var lower = code.ToLowerInvariant();

        // 檢查硬編碼密鑰
        if (lower.Contains("api_key") || lower.Contains("apikey") || lower.Contains("secret"))
        {
            issues.Add("安全性：檢查是否有硬編碼的 API 密鑰或密碼，建議使用環境變數");
        }

        // 檢查 SQL 注入風險
        if (language.Equals("csharp", StringComparison.OrdinalIgnoreCase) ||
            language.Equals("javascript", StringComparison.OrdinalIgnoreCase) ||
            language.Equals("python", StringComparison.OrdinalIgnoreCase))
        {
            if (lower.Contains("query") && lower.Contains("+ ") && lower.Contains("\"") && lower.Contains("where"))
            {
                issues.Add("安全性：潛在 SQL 注入風險，建議使用參數化查詢");
            }
        }

        // 檢查硬編碼 URL
        if (lower.Contains("http://") || lower.Contains("https://"))
        {
            issues.Add("安全性：檢查硬編碼的 URL 端點，建議從配置檔案讀取");
        }

        return issues;
    }

    private List<string> AnalyzePerformance(string code, string language)
    {
        var issues = new List<string>();

        // 檢查字符串串聯
        if (language.Equals("csharp", StringComparison.OrdinalIgnoreCase))
        {
            if (code.Contains("\" + \""))
            {
                issues.Add("效能：考慮使用 StringBuilder 或 string.Join 優化字符串串聯");
            }
        }

        // 檢查迴圈中過多的物件建立
        if (language.Equals("javascript", StringComparison.OrdinalIgnoreCase) ||
            language.Equals("typescript", StringComparison.OrdinalIgnoreCase))
        {
            if (code.Contains("new "))
            {
                issues.Add("效能：檢查迴圈中是否有不必要的物件建立");
            }
        }

        return issues;
    }

    private List<string> AnalyzeStyle(string code, string language)
    {
        var issues = new List<string>();

        // 檢查命名慣例
        if (language.Equals("csharp", StringComparison.OrdinalIgnoreCase))
        {
            if (code.Contains("var ") || code.Contains("int ") || code.Contains("string "))
            {
                issues.Add("樣式：C# 建議使用 PascalCase 命名慣例");
            }
        }
        else if (language.Equals("javascript", StringComparison.OrdinalIgnoreCase) ||
                 language.Equals("typescript", StringComparison.OrdinalIgnoreCase))
        {
            if (code.Contains("function ") || code.Contains("const ") || code.Contains("let "))
            {
                issues.Add("樣式：JavaScript/TypeScript 建議使用 camelCase 命名慣例");
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
            suggestions.Add("使用 dependency injection 管理依賴");
        }
        else if (language.Equals("javascript", StringComparison.OrdinalIgnoreCase) ||
                 language.Equals("typescript", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add("使用 TypeScript 提升型別安全性");
            suggestions.Add("考慮使用 async/await 處理非同步操作");
            suggestions.Add("檢查是否有適當的錯誤處理");
        }
        else if (language.Equals("python", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add("使用類型提示 (type hints) 提升程式碼可讀性");
            suggestions.Add("檢查是否有適當的錯誤處理 (try-except)");
            suggestions.Add("遵循 PEP 8 風格規範");
        }

        suggestions.Add("編寫單元測試以確保程式碼正確性");
        suggestions.Add("添加適當的日誌記錄以便除錯");
        suggestions.Add("定期重構以保持程式碼簡潔");

        return suggestions;
    }
}

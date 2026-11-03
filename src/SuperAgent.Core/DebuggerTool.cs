using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SuperAgent.Core;

namespace SuperAgent.Core;

/// <summary>
/// DebuggerTool - 除錯輔助工具
/// 協助分析錯誤並提供修復建議
/// </summary>
public class DebuggerTool : ITool
{
    public string Name => "debugger";
    public string Description => "分析錯誤訊息並提供修復建議與除錯步驟";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            errorMessage = new { type = "string", description = "錯誤訊息" },
            stackTrace = new { type = "string", description = "堆疊追蹤 (可選)" },
            code = new { type = "string", description = "相關程式碼 (可選)" },
            language = new { type = "string", description = "程式語言 (csharp, javascript, python, etc.)" }
        },
        required = new[] { "errorMessage" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var errorMessage = GetStringProperty(arguments, "errorMessage") ?? string.Empty;
        var stackTrace = GetStringProperty(arguments, "stackTrace") ?? string.Empty;
        var code = GetStringProperty(arguments, "code") ?? string.Empty;
        var language = GetStringProperty(arguments, "language") ?? "csharp";

        var result = AnalyzeError(errorMessage, stackTrace, code, language);
        return result;
    }

    private string AnalyzeError(string errorMessage, string stackTrace, string code, string language)
    {
        var lines = new List<string>();
        lines.Add("=== 錯誤分析結果 ===");
        lines.Add($"錯誤訊息: {errorMessage}");
        lines.Add("");

        // 錯誤類型分類
        var errorType = ClassifyError(errorMessage);
        lines.Add($"錯誤類型: {errorType}");
        lines.Add("");

        // 提供修復建議
        var suggestions = GetErrorFixSuggestions(errorType, errorMessage);
        if (suggestions.Count > 0)
        {
            lines.Add("=== 修復建議 ===");
            foreach (var suggestion in suggestions)
            {
                lines.Add($"🔧 {suggestion}");
            }
            lines.Add("");
        }

        // 堆疊追蹤分析
        if (!string.IsNullOrEmpty(stackTrace))
        {
            lines.Add("=== 堆疊追蹤分析 ===");
            var stackLines = stackTrace.Split('\n');
            foreach (var line in stackLines)
            {
                if (line.Contains("at ") || line.Contains("in "))
                {
                    lines.Add($"  {line.Trim()}");
                }
            }
            lines.Add("");
        }

        // 相關程式碼分析
        if (!string.IsNullOrEmpty(code))
        {
            lines.Add("=== 相關程式碼 ===");
            lines.Add(code);
            lines.Add("");
        }

        // 除錯步驟
        lines.Add("=== 除錯步驟 ===");
        lines.Add("1. 檢查錯誤訊息是否包含具體錯誤原因");
        lines.Add("2. 查看堆疊追蹤定位錯誤發生位置");
        lines.Add("3. 檢查相關程式碼的邏輯");
        lines.Add("4. 添加適當的錯誤處理 (try-catch)");
        lines.Add("5. 編寫單元測試覆蓋此錯誤場景");
        lines.Add("6. 使用日誌記錄關鍵變數值");

        return string.Join("\n", lines);
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

    private string ClassifyError(string errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
        {
            return "未知錯誤";
        }

        var lower = errorMessage.ToLowerInvariant();

        if (lower.Contains("null reference") || lower.Contains("nullreference"))
        {
            return "NullReferenceException - 空引用錯誤";
        }
        if (lower.Contains("index out of range") || lower.Contains("索引超出範圍"))
        {
            return "IndexOutOfRangeException - 索引超出範圍";
        }
        if (lower.Contains("file not found") || lower.Contains("檔案未找到"))
        {
            return "FileNotFoundException - 檔案未找到";
        }
        if (lower.Contains("timeout") || lower.Contains("超時"))
        {
            return "TimeoutException - 逾時錯誤";
        }
        if (lower.Contains("unauthorized") || lower.Contains("未授權"))
        {
            return "UnauthorizedAccessException - 未授權錯誤";
        }
        if (lower.Contains("format") || lower.Contains("格式"))
        {
            return "FormatException - 格式錯誤";
        }
        if (lower.Contains("argument") || lower.Contains("引數"))
        {
            return "ArgumentException - 引數錯誤";
        }

        return "UnknownError";
    }

    private List<string> GetErrorFixSuggestions(string errorType, string errorMessage)
    {
        var suggestions = new List<string>();

        if (errorType.Contains("NullReference"))
        {
            suggestions.Add("檢查變數是否已初始化");
            suggestions.Add("使用 null 檢查 (?:) 或 null 條件運算符");
            suggestions.Add("在使用前檢查物件是否為 null");
        }
        else if (errorType.Contains("IndexOutOfRange"))
        {
            suggestions.Add("檢查索引是否在範圍內");
            suggestions.Add("使用 Length 屬性檢查陣列長度");
            suggestions.Add("考慮使用 FirstOrDefault() 代替直接索引");
        }
        else if (errorType.Contains("FileNotFound"))
        {
            suggestions.Add("檢查檔案路徑是否正確");
            suggestions.Add("檢查檔案是否存在");
            suggestions.Add("檢查檔案存取權限");
        }
        else if (errorType.Contains("Timeout"))
        {
            suggestions.Add("增加超時時間");
            suggestions.Add("檢查網路連線");
            suggestions.Add("優化耗時操作");
        }
        else if (errorType.Contains("Unauthorized"))
        {
            suggestions.Add("檢查 API 密鑰是否正確");
            suggestions.Add("檢查權限是否足夠");
            suggestions.Add("檢查認證是否過期");
        }
        else
        {
            suggestions.Add("仔細閱讀錯誤訊息");
            suggestions.Add("檢查相關程式碼邏輯");
            suggestions.Add("添加錯誤處理與日誌");
        }

        return suggestions;
    }
}

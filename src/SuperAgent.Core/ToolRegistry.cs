using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SuperAgent.Core;

/// <summary>
/// ToolRegistry - 工具註冊與執行管理
/// </summary>
public class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _results = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 註冊工具
    /// </summary>
    public void Register(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    /// <summary>
    /// 取得所有工具定義
    /// </summary>
    public List<ToolDefinition> GetAllTools()
    {
        return _tools.Values
            .Select(t => new ToolDefinition
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.Parameters
            })
            .ToList();
    }

    /// <summary>
    /// 非同步執行工具
    /// </summary>
    public async Task<string> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
        {
            return $"Error: 工具 '{toolName}' 未找到";
        }

        return await tool.ExecuteAsync(arguments, cancellationToken);
    }

    /// <summary>
    /// 取得最後一個工具呼叫的結果
    /// </summary>
    public async Task<string?> GetLastResultAsync(string toolCallId)
    {
        return await Task.Run(() => _results.TryGetValue(toolCallId, out var result) ? result : null);
    }

    /// <summary>
    /// 設定工具呼叫結果
    /// </summary>
    public void SetResult(string toolCallId, string result)
    {
        _results[toolCallId] = result;
    }
}

/// <summary>
/// CodeWriter 工具 - 撰寫程式碼文件
/// </summary>
public class CodeWriterTool : ITool
{
    public string Name => "code_writer";
    public string Description => "撰寫或覆寫程式碼文件。適用於建立新程式或修改現有程式。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            file_path = new { type = "string", description = "要撰寫的文件路徑" },
            content = new { type = "string", description = "程式碼內容" }
        },
        required = new[] { "file_path", "content" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string filePath = arguments.GetProperty("file_path").GetString() ?? string.Empty;
        string content = arguments.GetProperty("content").GetString() ?? string.Empty;

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(filePath, content, cancellationToken);
            return $"Successfully wrote {filePath} ({content.Length} bytes)";
        }
        catch (Exception ex)
        {
            return $"Error writing file: {ex.Message}";
        }
    }
}

/// <summary>
/// CodeReader 工具 - 讀取程式碼文件
/// </summary>
public class CodeReaderTool : ITool
{
    public string Name => "code_reader";
    public string Description => "讀取程式碼文件內容。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            file_path = new { type = "string", description = "要讀取的文件路徑" }
        },
        required = new[] { "file_path" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string filePath = arguments.GetProperty("file_path").GetString() ?? string.Empty;

        try
        {
            if (!File.Exists(filePath))
            {
                return $"Error: File not found: {filePath}";
            }

            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            return $"File content ({content.Length} bytes):\n{content}";
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }
}

/// <summary>
/// ShellExec 工具 - 執行 shell 命令
/// </summary>
public class ShellExecTool : ITool
{
    public string Name => "shell_exec";
    public string Description => "執行系統 shell 命令。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            command = new { type = "string", description = "要執行的 shell 命令" },
            timeout_seconds = new { type = "number", description = "超時秒數，預設 30" }
        },
        required = new[] { "command" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string command = arguments.GetProperty("command").GetString() ?? string.Empty;
        int timeout = arguments.TryGetProperty("timeout_seconds", out var timeoutProp)
            ? (int)timeoutProp.GetDouble()
            : 30;

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "bash",
                Arguments = "-c \"" + command + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            System.Diagnostics.Process? process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return "Error: Unable to start process";
            }

            await process.WaitForExitAsync(cancellationToken);

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;

            var result = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(output))
            {
                result.AppendLine($"STDOUT:{output}");
            }
            if (!string.IsNullOrEmpty(error))
            {
                result.AppendLine($"STDERR:{error}");
            }
            result.AppendLine($"Exit code: {process.ExitCode}");

            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}";
        }
    }
}

/// <summary>
/// FileSearchTool 工具 - 搜尋檔案內容
/// </summary>
public class FileSearchTool : ITool
{
    public string Name => "file_search";
    public string Description => "在檔案系統中搜尋檔案內容。支援關鍵字搜尋與路徑篩選。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            directory = new { type = "string", description = "要搜尋的目錄路徑" },
            pattern = new { type = "string", description = "搜尋關鍵字或檔案模式" },
            file_extension = new { type = "string", description = "檔案副檔名（例如 .cs, .txt）" },
            max_results = new { type = "number", description = "最大回傳結果數，預設 20" }
        },
        required = new[] { "directory", "pattern" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string directory = arguments.GetProperty("directory").GetString() ?? Directory.GetCurrentDirectory();
        string pattern = arguments.GetProperty("pattern").GetString() ?? string.Empty;
        string fileExtension = arguments.TryGetProperty("file_extension", out var extProp)
            ? extProp.GetString() ?? string.Empty
            : string.Empty;
        int maxResults = arguments.TryGetProperty("max_results", out var maxProp)
            ? (int)maxProp.GetDouble()
            : 20;

        try
        {
            if (!Directory.Exists(directory))
            {
                return $"Error: Directory not found: {directory}";
            }

            var files = Directory.GetFiles(directory, "*" + fileExtension, SearchOption.AllDirectories);
            var results = new List<string>();

            foreach (var file in files)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file, cancellationToken);
                    if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(file);
                        if (results.Count >= maxResults)
                        {
                            break;
                        }
                    }
                }
                catch
                {
                    // 忽略無法讀取的文件
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"搜尋結果: {pattern}");
            sb.AppendLine($"找到 {results.Count} 個文件:");
            sb.AppendLine();

            foreach (var file in results)
            {
                sb.AppendLine($"  - {file}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error searching files: {ex.Message}";
        }
    }
}

/// <summary>
/// GitTool 工具 - Git 版本控制工具
/// </summary>
public class GitTool : ITool
{
    public string Name => "git_tool";
    public string Description => "執行 Git 版本控制命令。支援常見的 Git 操作。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            command = new { type = "string", description = "Git 命令 (例如 status, log, diff)" },
            args = new { type = "string", description = "額外參數" }
        },
        required = new[] { "command" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string command = arguments.GetProperty("command").GetString() ?? string.Empty;
        string args = arguments.TryGetProperty("args", out var argsProp)
            ? argsProp.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(command))
            return "Error: Git 命令不能為空";

        try
        {
            var fullCommand = $"git {command} {args}".Trim();

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = fullCommand,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            System.Diagnostics.Process? process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return "Error: Unable to start git process";
            }

            await process.WaitForExitAsync(cancellationToken);

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);

            var result = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(output))
            {
                result.AppendLine($"STDOUT:\n{output}");
            }
            if (!string.IsNullOrEmpty(error))
            {
                result.AppendLine($"STDERR:\n{error}");
            }
            result.AppendLine($"Exit code: {process.ExitCode}");

            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"Error executing git command: {ex.Message}";
        }
    }
}

/// <summary>
/// WebSearchTool - 網頁搜尋工具，支援搜尋引擎與頁面內容擷取
/// </summary>
public class WebSearchTool : ITool
{
    public string Name => "web_search";

    public string Description => "搜尋網頁內容。支援搜尋查詢與擷取網頁內容。適用於查詢資訊、查找文件與取得網頁內容。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "搜尋查詢字串" },
            max_results = new { type = "number", description = "最大結果數量 (預設 5)" },
            extract_content = new { type = "boolean", description = "是否擷取網頁內容 (預設 true)" }
        },
        required = new[] { "query" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string query = arguments.GetProperty("query").GetString() ?? string.Empty;
        int maxResults = arguments.TryGetProperty("max_results", out var maxProp)
            ? (int)maxProp.GetDouble()
            : 5;
        bool extractContent = arguments.TryGetProperty("extract_content", out var extractProp)
            ? extractProp.GetBoolean()
            : true;

        if (string.IsNullOrWhiteSpace(query))
        {
            return "Error: 搜尋查詢不能為空";
        }

        try
        {
            var results = new List<string>
            {
                $"搜尋 '{query}' 的相關資訊 (模擬結果)",
                $"  - 結果 1: 關於 {query} 的資訊頁面 (https://example.com/result1)",
                $"  - 結果 2: {query} 詳細說明 (https://example.com/result2)",
                $"  - 結果 3: {query} 教學指南 (https://example.com/result3)"
            };

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"搜尋查詢: {query}");
            sb.AppendLine($"找到 {maxResults} 個結果:");
            sb.AppendLine();

            for (int i = 0; i < Math.Min(maxResults, results.Count); i++)
            {
                sb.AppendLine($"[{i + 1}] {results[i]}");
                if (extractContent && i < 3)
                {
                    sb.AppendLine($"    內容摘要: 這是關於 {query} 的相關內容摘要...");
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error executing web search: {ex.Message}";
        }
    }
}

/// <summary>
/// CodeAnalysisTool - 程式碼分析工具，分析程式碼品質與潛在問題
/// </summary>
public class CodeAnalysisTool : ITool
{
    public string Name => "code_analysis";

    public string Description => "分析程式碼品質與潛在問題。支援檢查語法錯誤、命名規範、複雜度與安全性問題。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            file_path = new { type = "string", description = "要分析的程式碼文件路徑" },
            analysis_type = new { type = "string", description = "分析類型 (syntax, style, complexity, security, all)" }
        },
        required = new[] { "file_path" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string filePath = arguments.GetProperty("file_path").GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "Error: 文件路徑不能為空";
        }

        if (!File.Exists(filePath))
        {
            return $"Error: 文件 '{filePath}' 不存在";
        }

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"程式碼分析報告: {filePath}");
            sb.AppendLine();

            var styleIssues = CheckStyle(content);
            if (styleIssues.Any())
            {
                sb.AppendLine("=== 風格檢查 ===");
                foreach (var issue in styleIssues)
                {
                    sb.AppendLine($"  [建議] {issue}");
                }
            }
            else
            {
                sb.AppendLine("  程式碼風格良好");
            }

            var complexityIssues = CheckComplexity(content);
            if (complexityIssues.Any())
            {
                sb.AppendLine();
                sb.AppendLine("=== 複雜度檢查 ===");
                foreach (var issue in complexityIssues)
                {
                    sb.AppendLine($"  [資訊] {issue}");
                }
            }

            var securityIssues = CheckSecurity(content);
            if (securityIssues.Any())
            {
                sb.AppendLine();
                sb.AppendLine("=== 安全性檢查 ===");
                foreach (var issue in securityIssues)
                {
                    sb.AppendLine($"  [安全] {issue}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("分析完成");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error analyzing file: {ex.Message}";
        }
    }

    private List<string> CheckStyle(string content)
    {
        var issues = new List<string>();
        var lines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNum = i + 1;

            if (line.Length > 120)
            {
                issues.Add($"第 {lineNum} 行過長 ({line.Length} 字符)，建議拆分");
            }

            if (line.EndsWith(" ") || line.EndsWith("\t"))
            {
                issues.Add($"第 {lineNum} 行有尾隨空格");
            }
        }

        return issues;
    }

    private List<string> CheckComplexity(string content)
    {
        var issues = new List<string>();

        var ifCount = Regex.Matches(content, @"\bif\b").Count;
        if (ifCount > 5)
        {
            issues.Add($"if/else 數量較多 ({ifCount} 個 if)，建議考慮重構");
        }

        var methodCount = Regex.Matches(content, @"\b(?:public|private|protected|internal)\s+[\w<>]+\s+\w+\s*\(").Count;
        if (methodCount > 10)
        {
            issues.Add($"方法數量較多 ({methodCount} 個)，建議考慮拆分類別");
        }

        return issues;
    }

    private List<string> CheckSecurity(string content)
    {
        var issues = new List<string>();

        if (content.Contains("apiKey") || content.Contains("API_KEY") || content.Contains("password"))
        {
            issues.Add("發現可能硬編碼的敏感資訊，建議使用環境變數");
        }

        if (content.Contains("Process.Start") || content.Contains("shell_exec"))
        {
            issues.Add("發現進程執行相關程式碼，請確保輸入驗證充分");
        }

        return issues;
    }
}

/// <summary>
/// TestGeneratorTool - 自動產生單元測試工具
/// </summary>
public class TestGeneratorTool : ITool
{
    public string Name => "test_generator";

    public string Description => "自動產生單元測試。支援為現有程式碼產生 xunit 測試案例。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            file_path = new { type = "string", description = "要產生測試的程式碼文件路徑" },
            test_framework = new { type = "string", description = "測試框架 (xunit, nunit, mstest)" },
            target_class = new { type = "string", description = "目標類別名稱 (可選)" }
        },
        required = new[] { "file_path" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string filePath = arguments.GetProperty("file_path").GetString() ?? string.Empty;
        string testFramework = arguments.TryGetProperty("test_framework", out var fwProp)
            ? fwProp.GetString() ?? "xunit"
            : "xunit";
        string targetClass = arguments.TryGetProperty("target_class", out var tcProp)
            ? tcProp.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "Error: 文件路徑不能為空";
        }

        if (!File.Exists(filePath))
        {
            return $"Error: 文件 '{filePath}' 不存在";
        }

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var sb = new System.Text.StringBuilder();

            var className = Path.GetFileNameWithoutExtension(filePath);
            if (!string.IsNullOrEmpty(targetClass))
            {
                className = targetClass;
            }

            var testFilePath = filePath.Replace(".cs", ".Tests.cs");
            var testNamespace = "SuperAgent.Core.Tests";

            sb.AppendLine("// 自動產生的單元測試");
            sb.AppendLine("// 來源: " + filePath);
            sb.AppendLine();
            sb.AppendLine("using Xunit;");
            sb.AppendLine("using SuperAgent.Core;");
            sb.AppendLine();
            sb.AppendLine($"namespace {testNamespace};");
            sb.AppendLine();
            sb.AppendLine($"public class {className}Tests");
            sb.AppendLine("{");
            sb.AppendLine();

            var methods = ExtractPublicMethods(content);
            foreach (var method in methods)
            {
                sb.AppendLine($"    [Fact]");
                sb.AppendLine($"    public void {method.Name}_Test_Success()");
                sb.AppendLine($"    {{");
                sb.AppendLine($"        // Arrange");
                sb.AppendLine($"        var {className} = new {className}();");
                sb.AppendLine();
                sb.AppendLine($"        // Act");
                sb.AppendLine($"        var result = {className}.{method.Name}();");
                sb.AppendLine();
                sb.AppendLine($"        // Assert");
                sb.AppendLine($"        Assert.NotNull(result);");
                sb.AppendLine($"    }}");
                sb.AppendLine();
            }

            sb.AppendLine("}");

            var directory = Path.GetDirectoryName(testFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(testFilePath, sb.ToString(), cancellationToken);

            return $"Successfully generated test file: {testFilePath} ({sb.Length} bytes)";
        }
        catch (Exception ex)
        {
            return $"Error generating tests: {ex.Message}";
        }
    }

    private List<MethodInfo> ExtractPublicMethods(string content)
    {
        var methods = new List<MethodInfo>();
        var pattern = @"\bpublic\s+(?:[\w<>\[\]]+\s+)?(\w+)\s*\(([^)]*)\)";
        var matches = Regex.Matches(content, pattern);
        foreach (Match match in matches)
        {
            methods.Add(new MethodInfo
            {
                Name = match.Groups[1].Value,
                Parameters = match.Groups[2].Value
            });
        }
        return methods;
    }

    private class MethodInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Parameters { get; set; } = string.Empty;
    }
}

/// <summary>
/// DebuggerTool - 除錯工具，協助除錯與錯誤修復
/// </summary>
public class DebuggerTool : ITool
{
    public string Name => "debugger";

    public string Description => "協助除錯與錯誤修復。支援錯誤分析、程式碼修復建議與除錯策略。";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            error_message = new { type = "string", description = "錯誤訊息" },
            file_path = new { type = "string", description = "相關文件路徑 (可選)" },
            debug_level = new { type = "string", description = "除錯等級 (basic, detailed, full)" }
        },
        required = new[] { "error_message" }
    });

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        string errorMessage = arguments.GetProperty("error_message").GetString() ?? string.Empty;
        string filePath = arguments.TryGetProperty("file_path", out var fpProp)
            ? fpProp.GetString() ?? string.Empty
            : string.Empty;
        string debugLevel = arguments.TryGetProperty("debug_level", out var dlProp)
            ? dlProp.GetString() ?? "basic"
            : "basic";

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return "Error: 錯誤訊息不能為空";
        }

        try
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"除錯分析報告");
            sb.AppendLine($"錯誤訊息: {errorMessage}");
            sb.AppendLine();

            var errorType = ClassifyError(errorMessage);
            sb.AppendLine($"錯誤類型: {errorType}");
            sb.AppendLine();

            sb.AppendLine("=== 除錯建議 ===");
            var suggestions = GetDebugSuggestions(errorMessage, filePath, debugLevel);
            foreach (var suggestion in suggestions)
            {
                sb.AppendLine($"  - {suggestion}");
            }
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                sb.AppendLine("=== 文件分析 ===");
                var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                var fileSuggestions = AnalyzeFileForErrors(content, errorMessage);
                foreach (var suggestion in fileSuggestions)
                {
                    sb.AppendLine($"  - {suggestion}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("除錯分析完成");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error during debugging: {ex.Message}";
        }
    }

    private string ClassifyError(string errorMessage)
    {
        var lower = errorMessage.ToLowerInvariant();

        if (lower.Contains("nullreference") || lower.Contains("null reference"))
        {
            return "NullReferenceException - 空值參照錯誤";
        }
        if (lower.Contains("indexoutofrange") || lower.Contains("index out of range"))
        {
            return "IndexOutOfRangeException - 索引超出範圍錯誤";
        }
        if (lower.Contains("filenotfound") || lower.Contains("file not found"))
        {
            return "FileNotFoundException - 文件未找到錯誤";
        }
        if (lower.Contains("argumentexception") || lower.Contains("argument exception"))
        {
            return "ArgumentException - 引數錯誤";
        }
        if (lower.Contains("httprequest") || lower.Contains("http request"))
        {
            return "HttpRequestException - HTTP 請求錯誤";
        }
        if (lower.Contains("json") && lower.Contains("parse"))
        {
            return "JsonException - JSON 解析錯誤";
        }
        if (lower.Contains("timeout"))
        {
            return "TimeoutException - 超時錯誤";
        }

        return "UnknownError - 未知錯誤";
    }

    private List<string> GetDebugSuggestions(string errorMessage, string filePath, string debugLevel)
    {
        var suggestions = new List<string>();

        var lower = errorMessage.ToLowerInvariant();

        if (lower.Contains("nullreference"))
        {
            suggestions.Add("檢查變數是否為 null，使用 null 檢查或 null 合併運算子");
            suggestions.Add("在使用物件屬性前加入 null 檢查");
        }

        if (lower.Contains("indexoutofrange"))
        {
            suggestions.Add("檢查索引是否在陣列/列表範圍內");
            suggestions.Add("使用 FirstOrDefault() 或 TryGetValue() 避免拋出例外");
        }

        if (lower.Contains("filenotfound"))
        {
            suggestions.Add("確認文件路徑是否正確");
            suggestions.Add("檢查文件是否存在與權限是否足夠");
        }

        if (lower.Contains("httprequest"))
        {
            suggestions.Add("檢查網路連線與 API 端點是否正確");
            suggestions.Add("檢查 API 密鑰與認證資訊");
        }

        if (lower.Contains("json") && lower.Contains("parse"))
        {
            suggestions.Add("檢查 JSON 格式是否正確");
            suggestions.Add("使用 JsonDocument.TryParseElement() 安全解析");
        }

        if (lower.Contains("timeout"))
        {
            suggestions.Add("增加超時設定");
            suggestions.Add("檢查網路連線是否穩定");
        }

        if (suggestions.Count == 0)
        {
            suggestions.Add("請檢查錯誤堆疊追蹤以找出錯誤發生位置");
            suggestions.Add("加入日誌記錄以追蹤變數值");
        }

        if (debugLevel.Equals("detailed", StringComparison.OrdinalIgnoreCase) ||
            debugLevel.Equals("full", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add("考慮加入更詳細的日誌記錄");
            suggestions.Add("使用斷點除錯逐步執行程式碼");
            suggestions.Add("檢查相關的單元測試是否通過");
        }

        return suggestions;
    }

    private List<string> AnalyzeFileForErrors(string content, string errorMessage)
    {
        var suggestions = new List<string>();

        if (content.Contains(".") && errorMessage.Contains("NullReference"))
        {
            suggestions.Add("檢查此處是否有可能為 null 的物件引用");
        }

        if (content.Contains("["))
        {
            suggestions.Add("檢查陣列索引是否在範圍內");
        }

        if (content.Contains("File.Read") || content.Contains("File.Write"))
        {
            suggestions.Add("檢查文件操作是否有適當的錯誤處理");
        }

        return suggestions;
    }
}

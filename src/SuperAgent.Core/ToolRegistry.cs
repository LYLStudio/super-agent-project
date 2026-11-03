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

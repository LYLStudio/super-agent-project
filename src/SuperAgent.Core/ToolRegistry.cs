using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            return content;
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